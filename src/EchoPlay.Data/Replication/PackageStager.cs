using System.IO.Compression;
using System.Text;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Was ein Einspielen ersetzen würde: Bestand im Paket gegen Bestand jetzt.
    /// </summary>
    /// <param name="PackageSeries">Serien im Paket.</param>
    /// <param name="PackageEpisodes">Folgen im Paket.</param>
    /// <param name="PackageCompleted">Gehörte Folgen im Paket.</param>
    /// <param name="CurrentSeries">Serien jetzt.</param>
    /// <param name="CurrentEpisodes">Folgen jetzt.</param>
    /// <param name="CurrentCompleted">Gehörte Folgen jetzt.</param>
    public sealed record ImportPreview(
        int PackageSeries, int PackageEpisodes, int PackageCompleted,
        int CurrentSeries, int CurrentEpisodes, int CurrentCompleted);

    /// <summary>
    /// Ein geprüftes und entpacktes Paket, bereit zum Einspielen beim nächsten Start.
    /// </summary>
    /// <param name="Manifest">Das Manifest.</param>
    /// <param name="StagedDatabase">Die entpackte Datenbank neben der echten.</param>
    /// <param name="SealedCredentials">Die verschlüsselten Zugangsdaten; leer, wenn das Paket keine trägt.</param>
    /// <param name="Preview">Der Trockenlauf.</param>
    public sealed record StagedPackage(PackageManifest Manifest, string StagedDatabase, ReadOnlyMemory<byte> SealedCredentials, ImportPreview Preview);

    /// <summary>
    /// Ein Paket wurde abgelehnt; die Meldung sagt, warum.
    /// </summary>
    public sealed class PackageRejectedException : Exception
    {
        /// <summary>Erzeugt die Ausnahme.</summary>
        public PackageRejectedException()
        {
        }

        /// <summary>Erzeugt die Ausnahme mit Grund.</summary>
        /// <param name="message">Der Grund.</param>
        public PackageRejectedException(string message)
            : base(message)
        {
        }

        /// <summary>Erzeugt die Ausnahme mit Grund und Ursache.</summary>
        /// <param name="message">Der Grund.</param>
        /// <param name="innerException">Die Ursache.</param>
        public PackageRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Prüft ein Paket, entpackt die Datenbank neben die echte und rechnet den Trockenlauf.
    /// </summary>
    /// <remarks>
    /// Die laufende Datenbank kann nicht ersetzt werden, solange die Anwendung sie offen hat. Dieser
    /// Schritt bereitet deshalb nur vor; ersetzt wird beim nächsten Start
    /// (<see cref="PendingImportApplier"/>). Geprüft wird in dieser Reihenfolge, und jeder Schritt
    /// bricht ab: Archiv (<see cref="ArchiveGuard"/>), Manifest, Fassung, freier Platz, Länge und
    /// Prüfsumme beim Entpacken, SQLite-Kopf der entpackten Datei. Ein Paket aus einer neueren Fassung
    /// wird abgelehnt: Seine Datenbank kennt Migrationen, die diese Fassung nicht kennt, und ließe
    /// sich nicht zurückführen.
    /// </remarks>
    /// <param name="context">Der Datenbankkontext, für Migrationsstand und Trockenlauf.</param>
    /// <param name="databaseFile">Die Datei der Datenbank; daneben wird entpackt.</param>
    public sealed class PackageStager(EchoPlayDbContext context, DatabaseFileLocation databaseFile)
    {
        /// <summary>Name der entpackten Datenbank neben der echten.</summary>
        public const string StagedFileName = "echoplay.db.import";

        private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

        private const string CountSeries = """SELECT count(*) FROM "Series" WHERE NOT "IsDeleted";""";
        private const string CountEpisodes = """SELECT count(*) FROM "Episodes" WHERE NOT "IsDeleted";""";
        private const string CountCompleted = """SELECT count(*) FROM "PlaybackStates" WHERE NOT "IsDeleted" AND "IsCompleted";""";

        private readonly EchoPlayDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly DatabaseFileLocation _databaseFile = databaseFile ?? throw new ArgumentNullException(nameof(databaseFile));

        /// <summary>
        /// Prüft und entpackt ein Paket.
        /// </summary>
        /// <param name="packagePath">Das Paket.</param>
        /// <param name="cancellationToken">Bricht ab und räumt die entpackte Datei weg.</param>
        /// <returns>Das vorbereitete Paket.</returns>
        /// <exception cref="PackageRejectedException">Das Paket ist ungültig oder passt nicht zu dieser Fassung.</exception>
        /// <exception cref="InsufficientSpaceException">Neben der Datenbank ist zu wenig Platz.</exception>
        public async Task<StagedPackage> StageAsync(string packagePath, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

            string staged = Path.Combine(Path.GetDirectoryName(_databaseFile.FilePath)!, StagedFileName);
            FileStream packageStream = new(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using (packageStream.ConfigureAwait(false))
            {
                using ZipArchive archive = OpenArchive(packageStream);
                RejectUnsafeArchive(archive);

                PackageManifest manifest = await ReadManifestAsync(archive, cancellationToken).ConfigureAwait(false);
                RejectNewerVersion(manifest);

                PackageEntry databaseEntry = manifest.Entries.Single(e => e.Name == PackageFormat.DatabaseEntry);
                SpaceCheck.Require(Path.GetDirectoryName(staged)!, databaseEntry.Length + PackageExporter.SpaceMargin);

                try
                {
                    await ExtractVerifiedAsync(archive.GetEntry(PackageFormat.DatabaseEntry)!, databaseEntry, staged, cancellationToken).ConfigureAwait(false);
                    RequireSqliteHeader(staged);
                    ReadOnlyMemory<byte> credentials = await ReadCredentialsAsync(archive, manifest, cancellationToken).ConfigureAwait(false);
                    ImportPreview preview = await PreviewAsync(staged, cancellationToken).ConfigureAwait(false);
                    return new StagedPackage(manifest, staged, credentials, preview);
                }
                catch
                {
                    SqliteConnection.ClearAllPools();
                    File.Delete(staged);
                    throw;
                }
            }
        }

        /// <summary>Weist ein Archiv mit unsicherem Aufbau ab, bevor ein Eintrag gelesen wird.</summary>
        private static void RejectUnsafeArchive(ZipArchive archive)
        {
            string? archiveProblem = ArchiveGuard.Check(
                [.. archive.Entries.Select(e => new ArchiveEntryInfo(e.FullName, e.CompressedLength, e.Length))]);
            if (archiveProblem is not null)
            {
                throw new PackageRejectedException(archiveProblem);
            }
        }

        /// <summary>Öffnet das Paket als Zip-Archiv; eine Datei, die keines ist, wird abgewiesen.</summary>
        private static ZipArchive OpenArchive(Stream packageStream)
        {
            try
            {
                return new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException ex)
            {
                throw new PackageRejectedException("Die Datei ist kein Paket.", ex);
            }
        }

        /// <summary>Die geprüften Zugangsdaten des Pakets, oder leer, wenn es keine enthält.</summary>
        private static async Task<ReadOnlyMemory<byte>> ReadCredentialsAsync(ZipArchive archive, PackageManifest manifest, CancellationToken cancellationToken)
        {
            PackageEntry? credentialsEntry = manifest.Entries.SingleOrDefault(e => e.Name == PackageFormat.CredentialsEntry);
            return credentialsEntry is null
                ? ReadOnlyMemory<byte>.Empty
                : await ReadVerifiedAsync(archive.GetEntry(PackageFormat.CredentialsEntry), credentialsEntry, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<PackageManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            ZipArchiveEntry entry = archive.GetEntry(PackageFormat.ManifestEntry)!;
            byte[] data = await ReadBoundedAsync(entry, ArchiveGuard.MaxLengthOf(PackageFormat.ManifestEntry), cancellationToken).ConfigureAwait(false);
            string json;
            try
            {
                json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
            }
            catch (DecoderFallbackException ex)
            {
                throw new PackageRejectedException("Das Manifest ist kein UTF-8.", ex);
            }

            return PackageManifest.TryParse(json, out PackageManifest? manifest, out string? error)
                ? manifest!
                : throw new PackageRejectedException(error ?? "Manifest ungültig.");
        }

        private void RejectNewerVersion(PackageManifest manifest)
        {
            List<string> known = [.. _context.Database.GetMigrations()];
            if (!known.Contains(manifest.LastMigration, StringComparer.Ordinal))
            {
                string latest = known.Last();
                throw new PackageRejectedException(string.CompareOrdinal(manifest.LastMigration, latest) > 0
                    ? "Das Paket stammt aus einer neueren Fassung von EchoPlay. Bitte zuerst aktualisieren."
                    : "Das Paket nennt eine Migration, die diese Fassung nicht kennt.");
            }
        }

        /// <summary>
        /// Entpackt einen Eintrag und zählt selbst mit: Die Länge im Archiv ist nur eine Angabe, ein
        /// präpariertes Archiv kann mehr liefern. Länge und Prüfsumme müssen zum Manifest passen.
        /// </summary>
        private static async Task ExtractVerifiedAsync(ZipArchiveEntry entry, PackageEntry expected, string target, CancellationToken cancellationToken)
        {
            Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                FileStream output = new(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                await using (output.ConfigureAwait(false))
                {
                    string hash = await CopyBoundedAsync(source, output, expected.Length, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
                    {
                        throw new PackageRejectedException($"Die Prüfsumme von {expected.Name} stimmt nicht; das Paket ist beschädigt.");
                    }
                }
            }
        }

        private static async Task<byte[]> ReadVerifiedAsync(ZipArchiveEntry? entry, PackageEntry expected, CancellationToken cancellationToken)
        {
            if (entry is null)
            {
                throw new PackageRejectedException($"{expected.Name} steht im Manifest, fehlt aber im Paket.");
            }

            Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                MemoryStream buffer = new();
                await using (buffer.ConfigureAwait(false))
                {
                    string hash = await CopyBoundedAsync(source, buffer, expected.Length, cancellationToken).ConfigureAwait(false);
                    return string.Equals(hash, expected.Sha256, StringComparison.Ordinal)
                        ? buffer.ToArray()
                        : throw new PackageRejectedException($"Die Prüfsumme von {expected.Name} stimmt nicht; das Paket ist beschädigt.");
                }
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(ZipArchiveEntry entry, long maxLength, CancellationToken cancellationToken)
        {
            Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                MemoryStream buffer = new();
                await using (buffer.ConfigureAwait(false))
                {
                    _ = await CopyBoundedAsync(source, buffer, maxLength, cancellationToken, exactLength: false).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }
        }

        /// <summary>
        /// Kopiert höchstens <paramref name="length"/> Byte und bricht ab, wenn mehr kommt. Mit
        /// <paramref name="exactLength"/> muss es genau so viel sein.
        /// </summary>
        private static async Task<string> CopyBoundedAsync(Stream source, Stream target, long length, CancellationToken cancellationToken, bool exactLength = true)
            => (await HashingCopy.CopyAsync(source, target, length, exactLength, onCopied: null, cancellationToken).ConfigureAwait(false)).Sha256;

        private static void RequireSqliteHeader(string path)
        {
            Span<byte> header = stackalloc byte[16];
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Read(header) != header.Length || !header.SequenceEqual(SqliteHeader))
            {
                throw new PackageRejectedException("Die Datenbank im Paket ist keine SQLite-Datei.");
            }
        }

        private async Task<ImportPreview> PreviewAsync(string staged, CancellationToken cancellationToken)
        {
            (int series, int episodes, int completed) package;
            SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = staged,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                package = (
                    await CountAsync(connection, Count.Series, cancellationToken).ConfigureAwait(false),
                    await CountAsync(connection, Count.Episodes, cancellationToken).ConfigureAwait(false),
                    await CountAsync(connection, Count.Completed, cancellationToken).ConfigureAwait(false));
            }

            int currentSeries = await _context.Series.CountAsync(cancellationToken).ConfigureAwait(false);
            int currentEpisodes = await _context.Episodes.CountAsync(cancellationToken).ConfigureAwait(false);
            int currentCompleted = await _context.PlaybackStates.CountAsync(p => p.IsCompleted, cancellationToken).ConfigureAwait(false);

            return new ImportPreview(package.series, package.episodes, package.completed, currentSeries, currentEpisodes, currentCompleted);
        }

        /// <summary>Was der Trockenlauf im Paket zählt.</summary>
        private enum Count
        {
            /// <summary>Serien.</summary>
            Series,

            /// <summary>Folgen.</summary>
            Episodes,

            /// <summary>Gehörte Folgen.</summary>
            Completed,
        }

        private static async Task<int> CountAsync(SqliteConnection connection, Count what, CancellationToken cancellationToken)
        {
            SqliteCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                if (what == Count.Series)
                {
                    command.CommandText = CountSeries;
                }
                else if (what == Count.Episodes)
                {
                    command.CommandText = CountEpisodes;
                }
                else
                {
                    command.CommandText = CountCompleted;
                }
                object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
