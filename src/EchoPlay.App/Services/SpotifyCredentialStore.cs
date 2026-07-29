using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Speichert Spotify-Credentials DPAPI-verschlüsselt in der SQLite-Datenbank.
    /// DPAPI ist an den Windows-Benutzer gebunden — die Daten sind nur auf derselben
    /// Maschine und mit demselben Benutzerkonto entschlüsselbar.
    /// </summary>
    public sealed class SpotifyCredentialStore : ISpotifyCredentialStore
    {
        private const string KeyClientId = "Spotify:ClientId";
        private const string KeyClientSecret = "Spotify:ClientSecret";

        // Zusätzliche Entropie für DPAPI. Ohne sie kann jeder Prozess im selben
        // Benutzerkontext den Blob mit einem blanken Unprotect-Aufruf entschlüsseln —
        // genau das tun handelsübliche Credential-Stealer, die reihum über bekannte
        // Ablageorte laufen. Mit Entropie muss ein Angreifer diesen Wert kennen, also
        // gezielt gegen EchoPlay arbeiten. Die Entropie ist kein Geheimnis (sie steht im
        // Programmcode) und ersetzt keine Schlüsselverwaltung; sie hebt nur die Hürde
        // von "nebenbei mitgenommen" auf "gezielt gebaut".
        private static readonly byte[] ProtectionEntropy =
            Encoding.UTF8.GetBytes("EchoPlay.SpotifyCredentials.v1");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        private bool _hasCredentials;
        private bool _lastLoadFailedDueToCorruption;

        /// <summary>
        /// Erstellt einen neuen Credential-Store.
        /// </summary>
        public SpotifyCredentialStore(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("SpotifyCredentialStore");
        }

        /// <inheritdoc/>
        public bool HasCredentials => _hasCredentials;

        /// <inheritdoc/>
        public bool LastLoadFailedDueToCorruption => _lastLoadFailedDueToCorruption;

        /// <inheritdoc/>
        public void AcknowledgeCorruptionNotice() => _lastLoadFailedDueToCorruption = false;

        /// <inheritdoc/>
        public async Task<(string ClientId, string ClientSecret)?> GetAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            byte[]? encryptedId = await service.GetAsync(KeyClientId, cancellationToken);
            byte[]? encryptedSecret = await service.GetAsync(KeyClientSecret, cancellationToken);

            if (encryptedId is null || encryptedSecret is null)
            {
                return null;
            }

            try
            {
                (string clientId, bool idWasLegacy) = Decrypt(encryptedId);
                (string clientSecret, bool secretWasLegacy) = Decrypt(encryptedSecret);

                // Altbestand ohne Entropie: einmalig auf das neue Format heben, damit der
                // schwächere Blob nicht dauerhaft in der Datenbank liegen bleibt.
                if (idWasLegacy || secretWasLegacy)
                {
                    _logger.Info("Spotify-Credentials lagen im alten DPAPI-Format vor und werden neu verschlüsselt.");
                    await service.SaveAsync(KeyClientId, Encrypt(clientId), cancellationToken).ConfigureAwait(false);
                    await service.SaveAsync(KeyClientSecret, Encrypt(clientSecret), cancellationToken).ConfigureAwait(false);
                }

                return (clientId, clientSecret);
            }
            catch (CryptographicException ex)
            {
                // Nach Windows-Profil-Migration oder PC-Wechsel sind die Cipher-Bytes nicht mehr
                // entschlüsselbar. Ohne Aufräumen loggt jeder Start denselben Fehler — daher
                // löschen wir die korrupten Records und erzwingen eine Neu-Eingabe durch den Nutzer.
                _logger.Warning("Spotify-Credentials konnten nicht entschlüsselt werden ({Reason}). Korrupte Records werden entfernt.", ex.Message);

                await service.DeleteAsync(KeyClientId, cancellationToken).ConfigureAwait(false);
                await service.DeleteAsync(KeyClientSecret, cancellationToken).ConfigureAwait(false);

                _hasCredentials = false;
                _lastLoadFailedDueToCorruption = true;
                return null;
            }
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="clientId">Die vom Nutzer bei Spotify registrierte Client-ID.</param>
        /// <param name="clientSecret">Das zugehörige Client-Secret; wird nur verschlüsselt abgelegt.</param>
        public async Task SaveAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
        {
            byte[] encryptedId = Encrypt(clientId);
            byte[] encryptedSecret = Encrypt(clientSecret);

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            await service.SaveAsync(KeyClientId, encryptedId, cancellationToken);
            await service.SaveAsync(KeyClientSecret, encryptedSecret, cancellationToken);

            _hasCredentials = true;
            _lastLoadFailedDueToCorruption = false;
            _logger.Info("Spotify-Credentials gespeichert.");
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            await service.DeleteAsync(KeyClientId, cancellationToken);
            await service.DeleteAsync(KeyClientSecret, cancellationToken);

            _hasCredentials = false;
            _lastLoadFailedDueToCorruption = false;
            _logger.Info("Spotify-Credentials gelöscht.");
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            byte[]? encryptedId = await service.GetAsync(KeyClientId, cancellationToken);
            _hasCredentials = encryptedId is not null;
        }

        private static byte[] Encrypt(string plainText)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            return ProtectedData.Protect(plainBytes, ProtectionEntropy, DataProtectionScope.CurrentUser);
        }

        /// <summary>
        /// Entschlüsselt einen Credential-Blob und meldet, ob er noch im alten Format
        /// ohne zusätzliche Entropie vorlag.
        /// </summary>
        /// <param name="encryptedBytes">Der gespeicherte Cipher-Blob.</param>
        /// <returns>Klartext und ein Kennzeichen, ob der Blob aus dem Altbestand stammt.</returns>
        private static (string PlainText, bool WasLegacyFormat) Decrypt(byte[] encryptedBytes)
        {
            try
            {
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, ProtectionEntropy, DataProtectionScope.CurrentUser);
                return (Encoding.UTF8.GetString(plainBytes), false);
            }
            catch (CryptographicException)
            {
                // Vor der Entropie-Umstellung gespeicherte Credentials. Schlägt auch dieser
                // Versuch fehl, fliegt die Exception zum Aufrufer, der die Records aufräumt.
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return (Encoding.UTF8.GetString(plainBytes), true);
            }
        }
    }
}
