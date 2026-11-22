using System.IO.Compression;
using EchoPlay.Core.Replication.Packaging;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Packt die lokale Mediathek mit dem Datenpaket in ein Mediathek-Archiv.
    /// </summary>
    /// <remarks>
    /// Gestreamt, Datei für Datei: Im Speicher liegt nie mehr als ein Puffer und die Liste der Namen.
    /// Gespeichert wird ohne Kompression, denn Audiodateien und Cover sind schon komprimiert; ein
    /// zweites Packen kostete Minuten und spart nichts. Geschrieben wird in eine Zwischendatei, die
    /// erst am Ende ihren Namen bekommt, damit ein abgebrochenes Packen nie wie ein gültiges Archiv
    /// aussieht. Verknüpfungen (Reparse-Punkte) werden übergangen: Sie können aus der Mediathek
    /// hinaus oder im Kreis führen.
    /// </remarks>
    public static class LibraryArchiveWriter
    {
        /// <summary>
        /// Packt die Mediathek.
        /// </summary>
        /// <param name="libraryRoot">Stammpfad der Mediathek, absolut.</param>
        /// <param name="dataPackagePath">Das fertige Datenpaket, das mitgehen soll.</param>
        /// <param name="targetPath">Pfad des Archivs, absolut und außerhalb der Mediathek.</param>
        /// <param name="timeProvider">Zeitquelle für den Zeitpunkt im Manifest.</param>
        /// <param name="progress">Fortschritt von 0 bis 1 nach geschriebenen Byte; darf fehlen.</param>
        /// <param name="cancellationToken">Bricht das Packen ab; die Zwischendatei wird entfernt.</param>
        /// <returns>Das Manifest des geschriebenen Archivs.</returns>
        /// <exception cref="ArgumentException">Ein Pfad ist nicht absolut, das Ziel liegt in der Mediathek, oder ein Name ließe sich auf einem anderen Rechner nicht wieder anlegen.</exception>
        /// <exception cref="DirectoryNotFoundException">Den Stammpfad gibt es nicht.</exception>
        /// <exception cref="InsufficientSpaceException">Am Ziel ist zu wenig Platz.</exception>
        public static async Task<LibraryManifest> WriteAsync(
            string libraryRoot,
            string dataPackagePath,
            string targetPath,
            TimeProvider timeProvider,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            string root = AbsolutePath.Require(libraryRoot, nameof(libraryRoot));
            _ = AbsolutePath.Require(dataPackagePath, nameof(dataPackagePath));
            string target = AbsolutePath.Require(targetPath, nameof(targetPath));
            RequireRootOutsideTarget(root, target);

            (List<FileInfo> files, List<string> directories) = Enumerate(root);
            long dataLength = new FileInfo(dataPackagePath).Length;
            long total = files.Sum(f => f.Length) + dataLength;
            SpaceCheck.Require(Path.GetDirectoryName(target)!, total + PackageExporter.SpaceMargin);

            string partial = target + ".partial";
            try
            {
                LibraryManifest manifest = await WriteArchiveAsync(
                    new ArchiveContent(root, files, directories, dataPackagePath, timeProvider.GetUtcNow()), partial, total, progress, cancellationToken).ConfigureAwait(false);
                File.Move(partial, target, overwrite: true);
                progress?.Report(1);
                return manifest;
            }
            finally
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
        }

        /// <summary>
        /// Die Mediathek muss es geben, und das Archiv darf nicht in ihr liegen: Es packte sich
        /// sonst selbst mit ein.
        /// </summary>
        private static void RequireRootOutsideTarget(string root, string targetPath)
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Die Mediathek {root} gibt es nicht.");
            }

            if (targetPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Das Archiv darf nicht in der Mediathek selbst liegen.", nameof(targetPath));
            }
        }

        /// <summary>
        /// Sammelt alle Dateien und Ordner unter dem Stammpfad, in fester Reihenfolge, damit zwei
        /// Archive desselben Bestands gleich aufgebaut sind. Ein Name, der sich auf dem Zielrechner
        /// nicht anlegen ließe, bricht hier ab und nicht erst dort beim Auspacken.
        /// </summary>
        internal static (List<FileInfo> Files, List<string> Directories) Enumerate(string root)
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
            };

            DirectoryInfo rootInfo = new(root);
            List<FileInfo> files = [.. rootInfo.EnumerateFiles("*", options).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)];
            List<string> directories = [.. rootInfo.EnumerateDirectories("*", options)
                .Select(d => LibraryRelativePath.FromFullPath(root, d.FullName))
                .Order(StringComparer.OrdinalIgnoreCase)];

            string? unsafeName = files.Select(f => LibraryRelativePath.FromFullPath(root, f.FullName))
                .Concat(directories)
                .FirstOrDefault(relative => !LibraryRelativePath.IsSafe(relative));
            return unsafeName is null
                ? (files, directories)
                : throw new ArgumentException($"Der Name {unsafeName} ließe sich auf einem anderen Rechner nicht anlegen; bitte umbenennen.", nameof(root));
        }

        private static async Task<LibraryManifest> WriteArchiveAsync(
            ArchiveContent content,
            string partial,
            long total,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            long written = 0;
            void OnCopied(long bytes)
            {
                written += bytes;
                progress?.Report(total == 0 ? 0 : Math.Min(0.99, (double)written / total));
            }

            FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false);

                List<LibraryFile> packed = await AddLibraryFilesAsync(archive, content, OnCopied, cancellationToken).ConfigureAwait(false);

                foreach (string directory in content.Directories)
                {
                    _ = archive.CreateEntry(LibraryArchiveFormat.DirectoryEntryName(directory), CompressionLevel.NoCompression);
                }

                (long dataLength, string dataSha256) = await AddFileAsync(
                    archive, LibraryArchiveFormat.DataPackageEntry, content.DataPackagePath, File.GetLastWriteTime(content.DataPackagePath), OnCopied, cancellationToken).ConfigureAwait(false);

                LibraryManifest manifest = new(
                    LibraryArchiveFormat.CurrentFormatVersion, content.CreatedUtc, content.Root, packed, content.Directories,
                    new PackageEntry(LibraryArchiveFormat.DataPackageEntry, dataLength, dataSha256));
                await WriteManifestAsync(archive, manifest, cancellationToken).ConfigureAwait(false);
                return manifest;
            }
        }

        private static async Task<List<LibraryFile>> AddLibraryFilesAsync(
            ZipArchive archive,
            ArchiveContent content,
            Action<long> onCopied,
            CancellationToken cancellationToken)
        {
            List<LibraryFile> packed = new(content.Files.Count);
            foreach (FileInfo file in content.Files)
            {
                string relative = LibraryRelativePath.FromFullPath(content.Root, file.FullName);
                (long length, string sha256) = await AddFileAsync(archive, LibraryArchiveFormat.FileEntryName(relative), file.FullName, file.LastWriteTime, onCopied, cancellationToken).ConfigureAwait(false);
                packed.Add(new LibraryFile(relative, length, sha256));
            }

            return packed;
        }

        private static async Task WriteManifestAsync(ZipArchive archive, LibraryManifest manifest, CancellationToken cancellationToken)
        {
            ZipArchiveEntry manifestEntry = archive.CreateEntry(LibraryArchiveFormat.ManifestEntry, CompressionLevel.Optimal);
            Stream manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (manifestStream.ConfigureAwait(false))
            {
                await manifestStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifest.ToJson()), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Schreibt eine Datei als gespeicherten Eintrag. Gezählt wird, was tatsächlich gelesen
        /// wurde; ändert sich eine Datei während des Packens, stimmen Manifest und Inhalt trotzdem.
        /// </summary>
        private static async Task<(long Length, string Sha256)> AddFileAsync(
            ZipArchive archive, string name, string path, DateTimeOffset lastWrite, Action<long> onCopied, CancellationToken cancellationToken)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            entry.LastWriteTime = ClampToZipRange(lastWrite);
            FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using (input.ConfigureAwait(false))
            {
                Stream target = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (target.ConfigureAwait(false))
                {
                    return await HashingCopy.CopyAsync(input, target, LibraryArchiveFormat.MaxFileLength, exactLength: false, onCopied, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// ZIP kennt nur Zeitpunkte von 1980 bis 2107. Eine Datei mit einem Datum davor (das kommt
        /// bei kopierten CDs vor) bekäme sonst eine Ausnahme statt eines Eintrags.
        /// </summary>
        internal static DateTimeOffset ClampToZipRange(DateTimeOffset value)
        {
            DateTimeOffset earliest = new(1980, 1, 1, 0, 0, 0, value.Offset);
            DateTimeOffset latest = new(2107, 12, 31, 0, 0, 0, value.Offset);
            return value < earliest ? earliest : value > latest ? latest : value;
        }


        /// <summary>Was in ein Archiv geht und wann es gepackt wurde.</summary>
        private sealed record ArchiveContent(
            string Root, List<FileInfo> Files, List<string> Directories, string DataPackagePath, DateTimeOffset CreatedUtc);
    }
}
