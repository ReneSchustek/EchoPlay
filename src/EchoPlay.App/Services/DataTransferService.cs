using EchoPlay.Core;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Core.Security;
using EchoPlay.Data.Context;
using EchoPlay.Data.Replication;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Export und Einspielen des Datenbestands für einen Umzug.
    /// </summary>
    public interface IDataTransferService
    {
        /// <summary>
        /// Schreibt ein Paket in den gewählten Ordner.
        /// </summary>
        /// <param name="folder">Der Zielordner.</param>
        /// <param name="credentialsPassword">
        /// Kennwort für die Zugangsdaten; <see langword="null"/>, wenn sie nicht mitgehen sollen.
        /// </param>
        /// <param name="progress">Fortschritt von 0 bis 1.</param>
        /// <param name="cancellationToken">Bricht ab und räumt auf.</param>
        /// <returns>Pfad und Größe des Pakets.</returns>
        Task<(string Path, long Length)> ExportAsync(string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken);

        /// <summary>Prüft ein Paket und bereitet es vor.</summary>
        /// <param name="packagePath">Das Paket.</param>
        /// <param name="cancellationToken">Bricht ab und räumt auf.</param>
        /// <returns>Das vorbereitete Paket samt Trockenlauf.</returns>
        Task<StagedPackage> StageAsync(string packagePath, CancellationToken cancellationToken);

        /// <summary>
        /// Merkt das vorbereitete Paket für den nächsten Start vor.
        /// </summary>
        /// <param name="staged">Das vorbereitete Paket.</param>
        /// <param name="credentialsPassword">Kennwort für die Zugangsdaten im Paket, oder <see langword="null"/>.</param>
        /// <param name="libraryRoot">Neuer Stammpfad, wenn die Mediathek mit ausgepackt wurde; sonst <see langword="null"/>.</param>
        /// <returns><see langword="false"/>, wenn das Kennwort nicht passt; dann ist nichts vorgemerkt.</returns>
        bool ScheduleImport(StagedPackage staged, string? credentialsPassword, string? libraryRoot = null);

        /// <summary>Verwirft ein vorbereitetes Paket.</summary>
        void DiscardImport();
    }

    /// <summary>
    /// Standard-Implementierung von <see cref="IDataTransferService"/>.
    /// </summary>
    /// <remarks>
    /// Die Zugangsdaten sind die einzige Stelle, an der hier Klartext entsteht. Beim Export werden
    /// sie aus dem DPAPI-Speicher gelesen und sofort mit dem Kennwort verschlüsselt, beim Einspielen
    /// mit dem Kennwort entschlüsselt und sofort wieder per DPAPI verschlüsselt, für den Neustart in
    /// einer Datei neben der Datenbank. Auf der Platte liegen sie zu keinem Zeitpunkt im Klartext;
    /// das Kennwort wird weder gespeichert noch protokolliert.
    /// </remarks>
    /// <param name="scopeFactory">Für die Dienste des Datenprojekts.</param>
    /// <param name="credentialStore">Liest die Zugangsdaten dieses Rechners.</param>
    /// <param name="databaseFile">Die Datei der Datenbank; daneben liegen die Vormerkungen.</param>
    /// <param name="timeProvider">Ortszeit für den Dateinamen des Pakets; ohne Angabe die Systemuhr.</param>
    internal sealed class DataTransferService(
        IServiceScopeFactory scopeFactory,
        ISpotifyCredentialStore credentialStore,
        DatabaseFileLocation databaseFile,
        TimeProvider? timeProvider = null) : IDataTransferService
    {
        /// <summary>Zusätzliche Entropie für die DPAPI-Hülle der vorgemerkten Zugangsdaten.</summary>
        private static readonly byte[] PendingEntropy = "EchoPlay.Import.Credentials"u8.ToArray();

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private string DataDirectory => ParentFolder.Of(databaseFile.FilePath);

        /// <inheritdoc/>
        public async Task<(string Path, long Length)> ExportAsync(
            string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);

            byte[]? sealedCredentials = null;
            if (!string.IsNullOrEmpty(credentialsPassword))
            {
                (string ClientId, string ClientSecret)? credentials = await credentialStore.GetAsync(cancellationToken).ConfigureAwait(false);
                if (credentials is { } pair)
                {
                    byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new TransferredCredentials(pair.ClientId, pair.ClientSecret), JsonOptions);
                    try
                    {
                        sealedCredentials = CredentialSealer.Seal(plain, credentialsPassword);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plain);
                    }
                }
            }

            // Ortszeit, weil der Nutzer den Namen liest und ihn seinem Export zuordnen will.
            string fileName = "EchoPlay-Daten-" + (timeProvider ?? TimeProvider.System).GetLocalNow().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + PackageFormat.FileExtension;
            string target = Path.Combine(folder, fileName);

            using IServiceScope scope = scopeFactory.CreateScope();
            PackageExporter exporter = scope.ServiceProvider.GetRequiredService<PackageExporter>();
            long length = await exporter.ExportAsync(target, AppVersion, sealedCredentials, progress, cancellationToken).ConfigureAwait(false);
            return (target, length);
        }

        /// <inheritdoc/>
        public async Task<StagedPackage> StageAsync(string packagePath, CancellationToken cancellationToken)
        {
            DiscardImport();
            using IServiceScope scope = scopeFactory.CreateScope();
            PackageStager stager = scope.ServiceProvider.GetRequiredService<PackageStager>();
            return await stager.StageAsync(packagePath, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public bool ScheduleImport(StagedPackage staged, string? credentialsPassword, string? libraryRoot = null)
        {
            ArgumentNullException.ThrowIfNull(staged);

            if (!staged.SealedCredentials.IsEmpty && !string.IsNullOrEmpty(credentialsPassword))
            {
                if (!CredentialSealer.TryOpen(staged.SealedCredentials.Span, credentialsPassword, out byte[] plain))
                {
                    return false;
                }

                try
                {
                    byte[] wrapped = ProtectedData.Protect(plain, PendingEntropy, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(Path.Combine(DataDirectory, PendingImport.CredentialsFileName), wrapped);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }

            new PendingImport(staged.Manifest.LibraryRoot, staged.Manifest.DeviceId, libraryRoot).Write(DataDirectory);
            return true;
        }

        /// <inheritdoc/>
        public void DiscardImport() => PendingImport.Discard(DataDirectory);

        /// <summary>
        /// Legt nach dem Neustart vorgemerkte Zugangsdaten im Speicher dieses Rechners ab.
        /// </summary>
        /// <param name="dataDirectory">Das Datenverzeichnis.</param>
        /// <param name="store">Der Speicher der Zugangsdaten.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns><see langword="true"/>, wenn Zugangsdaten übernommen wurden.</returns>
        internal static async Task<bool> ApplyPendingCredentialsAsync(string dataDirectory, ISpotifyCredentialStore store, CancellationToken cancellationToken)
        {
            string path = Path.Combine(dataDirectory, PendingImport.CredentialsFileName);
            if (!File.Exists(path))
            {
                return false;
            }

            byte[] plain = [];
            try
            {
                byte[] wrapped = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                plain = ProtectedData.Unprotect(wrapped, PendingEntropy, DataProtectionScope.CurrentUser);
                TransferredCredentials? credentials = JsonSerializer.Deserialize<TransferredCredentials>(plain, JsonOptions);
                if (credentials is null || string.IsNullOrWhiteSpace(credentials.ClientId) || string.IsNullOrWhiteSpace(credentials.ClientSecret))
                {
                    return false;
                }

                await store.SaveAsync(credentials.ClientId, credentials.ClientSecret, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                // Eine Hülle, die dieser Benutzer nicht öffnen kann, stammt nicht von ihm. Sie wird
                // verworfen; die Zugangsdaten lassen sich in den Einstellungen neu eintragen.
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                File.Delete(path);
            }
        }

        private static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>Die Zugangsdaten, wie sie verschlüsselt ins Paket gehen.</summary>
        /// <param name="ClientId">Die Client-Kennung bei Spotify.</param>
        /// <param name="ClientSecret">Das Client-Geheimnis bei Spotify.</param>
        private sealed record TransferredCredentials(string ClientId, string ClientSecret);
    }
}
