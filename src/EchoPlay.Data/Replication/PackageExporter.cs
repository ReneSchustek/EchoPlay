using System.IO.Compression;
using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Schreibt den Datenbestand als Paket an einen frei gewählten Ort.
    /// </summary>
    /// <remarks>
    /// Das Paket entsteht aus einer Kopie per <c>VACUUM INTO</c>, nicht aus der laufenden Datei: Die
    /// Anwendung schreibt weiter, und eine Dateikopie im WAL-Modus erwischte einen halben Stand. In
    /// der Kopie werden die Zugangsdaten gelöscht, denn sie sind per DPAPI an diesen Rechner gebunden
    /// und anderswo wertlos; wer sie mitnehmen will, bekommt sie kennwortverschlüsselt als eigenen
    /// Eintrag. Geschrieben wird erst in eine Zwischendatei neben dem Ziel und am Ende umbenannt, damit
    /// ein abgebrochener Export kein halbes Paket unter dem richtigen Namen hinterlässt.
    /// </remarks>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="databaseFile">Die Datei der Datenbank.</param>
    /// <param name="device">Die Geräte-Kennung für das Manifest.</param>
    public sealed class PackageExporter(EchoPlayDbContext context, DatabaseFileLocation databaseFile, IDeviceIdentity device)
    {
        /// <summary>Spielraum über die Größe der Datenbank hinaus, den der Export am Ziel verlangt.</summary>
        internal const long SpaceMargin = 64L * 1024 * 1024;

        private const string RemoveCredentials = """DELETE FROM "SecureSettings";""";

        private readonly EchoPlayDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly DatabaseFileLocation _databaseFile = databaseFile ?? throw new ArgumentNullException(nameof(databaseFile));
        private readonly IDeviceIdentity _device = device ?? throw new ArgumentNullException(nameof(device));

        /// <summary>
        /// Schreibt das Paket.
        /// </summary>
        /// <param name="targetPath">Das Ziel, absolut; endet auf <see cref="PackageFormat.FileExtension"/>.</param>
        /// <param name="appVersion">Die Fassung der Anwendung für das Manifest.</param>
        /// <param name="sealedCredentials">
        /// Zugangsdaten aus <see cref="Core.Security.CredentialSealer.Seal"/>, oder <see langword="null"/>.
        /// </param>
        /// <param name="progress">Fortschritt von 0 bis 1.</param>
        /// <param name="cancellationToken">Bricht ab und räumt die Zwischendateien weg.</param>
        /// <returns>Die Größe des Pakets in Byte.</returns>
        /// <exception cref="InsufficientSpaceException">Am Ziel ist zu wenig Platz.</exception>
        public async Task<long> ExportAsync(
            string targetPath,
            string appVersion,
            byte[]? sealedCredentials,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ValidateArguments(targetPath, appVersion);
            (string lastMigration, string? root) = await ReadSchemaAndRootAsync(cancellationToken).ConfigureAwait(false);

            string snapshot = await SqliteSnapshot.CreateAsync(
                _databaseFile.FilePath, $"echoplay.db.export-{Guid.NewGuid():N}.tmp", cancellationToken).ConfigureAwait(false);
            string partial = targetPath + ".partial";
            try
            {
                await RemoveCredentialsAsync(snapshot, cancellationToken).ConfigureAwait(false);

                SpaceCheck.Require(Path.GetDirectoryName(targetPath)!, new FileInfo(snapshot).Length + SpaceMargin);

                await WritePackageAsync(
                    partial,
                    snapshot,
                    sealedCredentials,
                    entries => CreateManifest(appVersion, lastMigration, root, entries),
                    progress,
                    cancellationToken).ConfigureAwait(false);

                File.Move(partial, targetPath, overwrite: true);
                progress?.Report(1);
                return new FileInfo(targetPath).Length;
            }
            finally
            {
                DeleteQuietly(snapshot);
                DeleteQuietly(partial);
            }
        }

        private static void ValidateArguments(string targetPath, string appVersion)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
            if (!Path.IsPathFullyQualified(targetPath))
            {
                throw new ArgumentException("Das Ziel muss ein absoluter Pfad sein.", nameof(targetPath));
            }
        }

        private PackageManifest CreateManifest(string appVersion, string lastMigration, string? root, List<PackageEntry> entries) =>
            new(
                PackageFormat.CurrentFormatVersion, appVersion, lastMigration, _device.DeviceId,
                DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(root) ? null : root, entries);

        /// <summary>Liest die zuletzt eingespielte Migration und den Stammpfad der Mediathek.</summary>
        private async Task<(string LastMigration, string? Root)> ReadSchemaAndRootAsync(CancellationToken cancellationToken)
        {
            string lastMigration = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Last();
            string? root = await _context.AppSettings
                .AsNoTracking()
                .Select(s => s.LocalLibraryRootPath)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return (lastMigration, root);
        }

        private static async Task RemoveCredentialsAsync(string snapshot, CancellationToken cancellationToken)
        {
            SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString());
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                SqliteCommand command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText = RemoveCredentials;
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Schreibt Datenbank, versiegelte Zugangsdaten und zuletzt das Manifest, das die beiden
        /// mit Länge und Prüfsumme beschreibt.
        /// </summary>
        private static async Task WritePackageAsync(
            string partial,
            string snapshot,
            byte[]? sealedCredentials,
            Func<List<PackageEntry>, PackageManifest> manifestFor,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            List<PackageEntry> entries = [];
            FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (output.ConfigureAwait(false))
            {
                using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false);

                entries.Add(await WriteFileAsync(archive, PackageFormat.DatabaseEntry, snapshot, new FileInfo(snapshot).Length, progress, cancellationToken).ConfigureAwait(false));
                if (sealedCredentials is { Length: > 0 })
                {
                    entries.Add(await WriteBytesAsync(archive, PackageFormat.CredentialsEntry, sealedCredentials, cancellationToken).ConfigureAwait(false));
                }

                PackageManifest manifest = manifestFor(entries);
                _ = await WriteBytesAsync(archive, PackageFormat.ManifestEntry, System.Text.Encoding.UTF8.GetBytes(manifest.ToJson()), cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<PackageEntry> WriteFileAsync(
            ZipArchive archive, string name, string path, long length, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using (input.ConfigureAwait(false))
            {
                return await WriteStreamAsync(archive, name, input, length, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<PackageEntry> WriteBytesAsync(ZipArchive archive, string name, byte[] data, CancellationToken cancellationToken)
        {
            MemoryStream input = new(data, writable: false);
            await using (input.ConfigureAwait(false))
            {
                return await WriteStreamAsync(archive, name, input, data.Length, progress: null, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Schreibt einen Eintrag und rechnet dabei die Prüfsumme mit. Bilddaten packen sich kaum;
        /// die schnellste Stufe spart deshalb Minuten ohne messbaren Größenverlust.
        /// </summary>
        private static async Task<PackageEntry> WriteStreamAsync(
            ZipArchive archive, string name, Stream input, long length, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            Stream target = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (target.ConfigureAwait(false))
            {
                long written = 0;
                Action<long>? onCopied = progress is null || length <= 0
                    ? null
                    : copied =>
                    {
                        written += copied;
                        progress.Report(Math.Min(0.99, (double)written / length));
                    };
                (long total, string sha256) = await HashingCopy.CopyAsync(
                    input, target, long.MaxValue, exactLength: false, onCopied, cancellationToken).ConfigureAwait(false);
                return new PackageEntry(name, total, sha256);
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Eine liegengebliebene Zwischendatei ist ärgerlich, aber kein Grund, einen
                // gelungenen Export als gescheitert zu melden.
            }
        }
    }
}
