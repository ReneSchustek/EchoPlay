using System.IO.Compression;
using System.Text;
using EchoPlay.Core.Replication.Packaging;

namespace EchoPlay.Data.Replication
{
    /// <summary>Was das Auspacken eines Mediathek-Archivs geschrieben hat.</summary>
    /// <param name="Manifest">Das Manifest des Archivs.</param>
    /// <param name="TargetRoot">Der Ordner, in dem die Mediathek jetzt liegt.</param>
    /// <param name="DataPackagePath">Das ausgepackte Datenpaket, bereit zum Einspielen.</param>
    /// <param name="CreatedTargetRoot">Ob der Ordner erst beim Auspacken entstand.</param>
    public sealed record LibraryExtraction(LibraryManifest Manifest, string TargetRoot, string DataPackagePath, bool CreatedTargetRoot);

    /// <summary>
    /// Packt ein Mediathek-Archiv in einen leeren Ordner aus.
    /// </summary>
    /// <remarks>
    /// Das Archiv ist eine fremde Datei. Es wird geprüft, bevor ein Byte geschrieben wird
    /// (<see cref="LibraryArchiveGuard"/>, <see cref="LibraryManifest"/>), und jede Datei beim
    /// Entpacken gegen Länge und Prüfsumme gehalten. Der Zielordner muss leer sein: Dann gehört alles
    /// darin zu diesem Auspacken, und ein Fehler oder Abbruch kann es vollständig wegräumen, ohne je
    /// eine fremde Datei zu berühren.
    /// </remarks>
    public static class LibraryArchiveExtractor
    {
        /// <summary>
        /// Packt das Archiv aus.
        /// </summary>
        /// <param name="archivePath">Das Mediathek-Archiv.</param>
        /// <param name="targetRoot">Der Zielordner, absolut; leer oder noch nicht vorhanden.</param>
        /// <param name="dataPackagePath">Wohin das Datenpaket ausgepackt wird.</param>
        /// <param name="progress">Fortschritt von 0 bis 1 nach geschriebenen Byte; darf fehlen.</param>
        /// <param name="cancellationToken">Bricht ab; Geschriebenes wird entfernt.</param>
        /// <returns>Was ausgepackt wurde.</returns>
        /// <exception cref="ArgumentException">Ein Pfad ist nicht absolut.</exception>
        /// <exception cref="IOException">Der Zielordner ist nicht leer.</exception>
        /// <exception cref="PackageRejectedException">Das Archiv ist ungültig oder beschädigt.</exception>
        /// <exception cref="InsufficientSpaceException">Am Ziel ist zu wenig Platz.</exception>
        public static async Task<LibraryExtraction> ExtractAsync(
            string archivePath,
            string targetRoot,
            string dataPackagePath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
            string root = AbsolutePath.Require(targetRoot, nameof(targetRoot));
            string dataTarget = AbsolutePath.Require(dataPackagePath, nameof(dataPackagePath));
            PrepareTargets(root, dataTarget);

            FileStream input = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using (input.ConfigureAwait(false))
            {
                using ZipArchive archive = OpenArchive(input);
                LibraryManifest manifest = await ReadCheckedManifestAsync(archive, cancellationToken).ConfigureAwait(false);
                SpaceCheck.Require(root, manifest.TotalFileLength + PackageExporter.SpaceMargin);
                SpaceCheck.Require(Path.GetDirectoryName(dataTarget)!, manifest.DataPackage.Length + PackageExporter.SpaceMargin);

                bool created = !Directory.Exists(root);
                try
                {
                    await WriteContentAsync(archive, manifest, root, dataTarget, progress, cancellationToken).ConfigureAwait(false);
                    progress?.Report(1);
                    return new LibraryExtraction(manifest, root, dataTarget, created);
                }
                catch
                {
                    RemoveExtracted(root, created);
                    DeleteIfExists(dataTarget);
                    throw;
                }
            }
        }

        /// <summary>
        /// Räumt ein liegengebliebenes Datenpaket weg und besteht auf einem leeren Zielordner.
        /// </summary>
        private static void PrepareTargets(string root, string dataTarget)
        {
            DeleteIfExists(dataTarget);

            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new IOException($"Der Zielordner {root} ist nicht leer. Bitte einen leeren oder neuen Ordner wählen.");
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Entfernt, was ein Auspacken geschrieben hat: den ganzen Ordner, wenn er dabei entstand,
        /// sonst seinen Inhalt. Der Ordner war vorher leer; fremde Dateien gibt es darin nicht.
        /// </summary>
        /// <param name="targetRoot">Der Zielordner des Auspackens.</param>
        /// <param name="createdTargetRoot">Ob der Ordner beim Auspacken entstand.</param>
        public static void RemoveExtracted(string targetRoot, bool createdTargetRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
            if (!Directory.Exists(targetRoot))
            {
                return;
            }

            if (createdTargetRoot)
            {
                Directory.Delete(targetRoot, recursive: true);
                return;
            }

            DirectoryInfo root = new(targetRoot);
            foreach (FileInfo file in root.EnumerateFiles())
            {
                file.Delete();
            }

            foreach (DirectoryInfo directory in root.EnumerateDirectories())
            {
                directory.Delete(recursive: true);
            }
        }

        private static ZipArchive OpenArchive(Stream input)
        {
            try
            {
                return new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException ex)
            {
                throw new PackageRejectedException("Die Datei ist kein Mediathek-Archiv.", ex);
            }
        }

        /// <summary>Prüft den Aufbau, liest das Manifest und gleicht beides ab.</summary>
        private static async Task<LibraryManifest> ReadCheckedManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            List<ArchiveEntryInfo> entries = [.. archive.Entries.Select(e => new ArchiveEntryInfo(e.FullName, e.CompressedLength, e.Length))];
            string? problem = LibraryArchiveGuard.CheckStructure(entries);
            if (problem is not null)
            {
                throw new PackageRejectedException(problem);
            }

            string json = await ReadManifestTextAsync(archive.GetEntry(LibraryArchiveFormat.ManifestEntry)!, cancellationToken).ConfigureAwait(false);
            if (!LibraryManifest.TryParse(json, out LibraryManifest? manifest, out string? error))
            {
                throw new PackageRejectedException(error ?? "Manifest ungültig.");
            }

            problem = LibraryArchiveGuard.CheckAgainstManifest(entries, manifest!);
            return problem is null ? manifest! : throw new PackageRejectedException(problem);
        }

        /// <summary>
        /// Liest das Manifest als strenges UTF-8, höchstens bis zur vierfachen Grenze des Formats.
        /// </summary>
        private static async Task<string> ReadManifestTextAsync(ZipArchiveEntry manifestEntry, CancellationToken cancellationToken)
        {
            MemoryStream buffer = new();
            await using (buffer.ConfigureAwait(false))
            {
                Stream source = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    _ = await HashingCopy.CopyAsync(source, buffer, LibraryArchiveFormat.MaxManifestLength * 4L, exactLength: false, onCopied: null, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new PackageRejectedException("Das Manifest ist kein UTF-8.", ex);
                }
            }
        }

        private static async Task WriteContentAsync(
            ZipArchive archive, LibraryManifest manifest, string root, string dataTarget, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long total = manifest.TotalFileLength + manifest.DataPackage.Length;
            long written = 0;
            void OnCopied(long bytes)
            {
                written += bytes;
                progress?.Report(total == 0 ? 0 : Math.Min(0.99, (double)written / total));
            }

            _ = Directory.CreateDirectory(root);
            foreach (string directory in manifest.Directories)
            {
                _ = Directory.CreateDirectory(LibraryRelativePath.Resolve(root, directory));
            }

            foreach (LibraryFile file in manifest.Files)
            {
                string path = LibraryRelativePath.Resolve(root, file.Path);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ZipArchiveEntry entry = archive.GetEntry(LibraryArchiveFormat.FileEntryName(file.Path))!;
                await ExtractVerifiedAsync(entry, file.Path, file.Length, file.Sha256, path, OnCopied, cancellationToken).ConfigureAwait(false);
                File.SetLastWriteTime(path, entry.LastWriteTime.LocalDateTime);
            }

            PackageEntry data = manifest.DataPackage;
            ZipArchiveEntry dataEntry = archive.GetEntry(LibraryArchiveFormat.DataPackageEntry)!;
            await ExtractVerifiedAsync(dataEntry, data.Name, data.Length, data.Sha256, dataTarget, OnCopied, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Entpackt einen Eintrag; Länge und Prüfsumme müssen zum Manifest passen, sonst ist das
        /// Archiv beschädigt und die Meldung nennt die Datei.
        /// </summary>
        private static async Task ExtractVerifiedAsync(
            ZipArchiveEntry entry, string name, long length, string sha256, string target, Action<long> onCopied, CancellationToken cancellationToken)
        {
            Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                await using (output.ConfigureAwait(false))
                {
                    (_, string hash) = await HashingCopy.CopyAsync(source, output, length, exactLength: true, onCopied, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(hash, sha256, StringComparison.Ordinal))
                    {
                        throw new PackageRejectedException($"Die Prüfsumme von {name} stimmt nicht; das Archiv ist beschädigt.");
                    }
                }
            }
        }
    }
}
