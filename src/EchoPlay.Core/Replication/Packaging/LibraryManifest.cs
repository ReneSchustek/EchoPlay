using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>Eine Datei der Mediathek im Archiv.</summary>
    /// <param name="Path">Relativer Pfad mit <c>/</c> als Trenner.</param>
    /// <param name="Length">Länge in Byte.</param>
    /// <param name="Sha256">SHA-256 des Inhalts, kleingeschrieben.</param>
    public sealed record LibraryFile(string Path, long Length, string Sha256);

    /// <summary>
    /// Das Manifest eines Mediathek-Archivs: woher es stammt, welche Dateien und Ordner es trägt und
    /// welches Datenpaket dazugehört.
    /// </summary>
    /// <param name="FormatVersion">Version des Formats.</param>
    /// <param name="CreatedUtc">Zeitpunkt des Packens.</param>
    /// <param name="SourceRoot">Stammpfad der Mediathek auf dem Absender.</param>
    /// <param name="Files">Die Dateien.</param>
    /// <param name="Directories">Alle Ordner, auch leere, damit die Struktur vollständig wiederkommt.</param>
    /// <param name="DataPackage">Länge und Prüfsumme des eingebetteten Datenpakets.</param>
    public sealed record LibraryManifest(
        int FormatVersion,
        DateTimeOffset CreatedUtc,
        string SourceRoot,
        IReadOnlyList<LibraryFile> Files,
        IReadOnlyList<string> Directories,
        PackageEntry DataPackage)
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        };

        private static readonly Regex HexSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>Gesamtlänge aller Dateien in Byte.</summary>
        public long TotalFileLength => Files.Sum(f => f.Length);

        /// <summary>Schreibt das Manifest als JSON.</summary>
        /// <returns>Das JSON.</returns>
        public string ToJson() => JsonSerializer.Serialize(new ManifestDocument(
            FormatVersion,
            CreatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            SourceRoot,
            [.. Files.Select(f => new FileDocument(f.Path, f.Length, f.Sha256))],
            [.. Directories],
            new FileDocument(DataPackage.Name, DataPackage.Length, DataPackage.Sha256)), Options);

        /// <summary>
        /// Liest ein Manifest streng: Jedes Feld wird geprüft, jeder Pfad muss sicher sein, und kein
        /// Pfad darf doppelt vorkommen, auch nicht in anderer Groß- und Kleinschreibung.
        /// </summary>
        /// <param name="json">Der Inhalt von <see cref="LibraryArchiveFormat.ManifestEntry"/>.</param>
        /// <param name="manifest">Das gelesene Manifest.</param>
        /// <param name="error">Der Grund der Ablehnung.</param>
        /// <returns><see langword="true"/>, wenn das Manifest gültig ist.</returns>
        public static bool TryParse(string? json, out LibraryManifest? manifest, out string? error)
        {
            manifest = null;
            error = StrictJson.TryDeserialize(
                json, LibraryArchiveFormat.MaxManifestLength, Options,
                "Manifest fehlt oder ist zu groß.", "Manifest ist kein gültiges JSON.",
                out ManifestDocument? document)
                ?? Validate(document);
            if (error is not null)
            {
                return false;
            }

            manifest = new LibraryManifest(
                document!.FormatVersion,
                DateTimeOffset.Parse(document.CreatedUtc!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                document.SourceRoot!,
                [.. document.Files!.Select(f => new LibraryFile(f.Path!, f.Length, f.Sha256!))],
                [.. document.Directories!],
                new PackageEntry(LibraryArchiveFormat.DataPackageEntry, document.DataPackage!.Length, document.DataPackage.Sha256!));
            return true;
        }

        private static string? Validate(ManifestDocument? d)
        {
            if (d is null)
            {
                return "Manifest ist leer.";
            }

            if (d.FormatVersion != LibraryArchiveFormat.CurrentFormatVersion)
            {
                return $"Archivformat {d.FormatVersion} wird nicht unterstützt.";
            }

            if (!DateTimeOffset.TryParse(d.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                return "Zeitpunkt des Packens fehlt.";
            }

            if (string.IsNullOrWhiteSpace(d.SourceRoot) || d.SourceRoot.Length > 1024 || !Path.IsPathFullyQualified(d.SourceRoot))
            {
                return "Stammpfad im Manifest ist nicht absolut.";
            }

            if (d.DataPackage is null || !IsValidLength(d.DataPackage.Length, LibraryArchiveFormat.MaxDataPackageLength) || !IsValidHash(d.DataPackage.Sha256))
            {
                return "Datenpaket ohne gültige Länge oder Prüfsumme.";
            }

            return ValidateFiles(d.Files) ?? ValidateDirectories(d.Directories, d.Files!);
        }

        private static string? ValidateFiles(List<FileDocument>? files)
        {
            if (files is null || files.Count > LibraryArchiveFormat.MaxFiles)
            {
                return "Dateiliste fehlt oder ist zu lang.";
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (FileDocument file in files)
            {
                if (!LibraryRelativePath.IsSafe(file.Path) || !seen.Add(file.Path!))
                {
                    return $"Unzulässiger oder doppelter Pfad: {file.Path}";
                }

                if (!IsValidLength(file.Length, LibraryArchiveFormat.MaxFileLength) || !IsValidHash(file.Sha256))
                {
                    return $"Datei {file.Path} ohne gültige Länge oder Prüfsumme.";
                }
            }

            return null;
        }

        private static string? ValidateDirectories(List<string>? directories, List<FileDocument> files)
        {
            if (directories is null || directories.Count > LibraryArchiveFormat.MaxDirectories)
            {
                return "Ordnerliste fehlt oder ist zu lang.";
            }

            // Ein Ordner mit dem Namen einer Datei ließe sich nicht anlegen; Dateien und Ordner teilen
            // sich deshalb eine Menge.
            HashSet<string> seen = new(files.Select(f => f.Path!), StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
            {
                if (!LibraryRelativePath.IsSafe(directory) || !seen.Add(directory))
                {
                    return $"Unzulässiger oder doppelter Ordner: {directory}";
                }
            }

            return null;
        }

        private static bool IsValidLength(long length, long max) => length >= 0 && length <= max;

        private static bool IsValidHash(string? sha256) => sha256 is not null && HexSha256.IsMatch(sha256);

        /// <summary>Das Manifest, wie es als JSON im Archiv steht; jedes Feld darf fehlen und wird geprüft.</summary>
        private sealed record ManifestDocument(
            int FormatVersion,
            string? CreatedUtc,
            string? SourceRoot,
            List<FileDocument>? Files,
            List<string>? Directories,
            FileDocument? DataPackage);

        /// <summary>Eine Datei oder das Datenpaket, wie es als JSON im Manifest steht.</summary>
        private sealed record FileDocument(string? Path, long Length, string? Sha256);
    }
}
