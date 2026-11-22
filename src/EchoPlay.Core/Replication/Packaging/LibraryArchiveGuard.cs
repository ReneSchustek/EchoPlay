namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Prüft ein Mediathek-Archiv in zwei Schritten, bevor ein Byte daraus geschrieben wird.
    /// </summary>
    /// <remarks>
    /// Zuerst nur der Aufbau, ohne Manifest: Zahl der Einträge, Manifest und Datenpaket vorhanden,
    /// jeder Name von bekannter Form. Danach der Abgleich mit dem Manifest: Jeder Eintrag steht dort,
    /// jede Datei hat die angegebene Länge, nichts fehlt und nichts ist zusätzlich darin. Erst damit
    /// steht fest, dass das Auspacken genau die Dateien des Manifests schreibt.
    /// </remarks>
    public static class LibraryArchiveGuard
    {
        /// <summary>Feste Einträge neben der Mediathek: Manifest und Datenpaket.</summary>
        private const int FixedEntries = 2;

        /// <summary>Ab dieser Länge wird das Verhältnis entpackt zu gepackt geprüft.</summary>
        private const long RatioThreshold = 1024 * 1024;

        /// <summary>
        /// Prüft den Aufbau eines Archivs, bevor das Manifest gelesen wird.
        /// </summary>
        /// <param name="entries">Die Einträge, wie das Archiv sie meldet.</param>
        /// <returns>Der Grund der Ablehnung, oder <see langword="null"/>.</returns>
        public static string? CheckStructure(IReadOnlyList<ArchiveEntryInfo> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count < FixedEntries || entries.Count > LibraryArchiveFormat.MaxFiles + LibraryArchiveFormat.MaxDirectories + FixedEntries)
            {
                return $"Das Archiv hat {entries.Count} Einträge; das passt zu keinem Mediathek-Archiv.";
            }

            ArchiveEntryInfo? manifest = entries.FirstOrDefault(e => e.FullName == LibraryArchiveFormat.ManifestEntry);
            if (manifest is null || entries.All(e => e.FullName != LibraryArchiveFormat.DataPackageEntry))
            {
                return "Manifest oder Datenpaket fehlen im Archiv.";
            }

            if (manifest.Length < 0 || manifest.Length > LibraryArchiveFormat.MaxManifestLength * 4L)
            {
                return "Das Manifest ist zu groß.";
            }

            ArchiveEntryInfo? unknown = entries.FirstOrDefault(e => !HasKnownForm(e.FullName));
            return unknown is null ? null : $"Unerwarteter Eintrag: {unknown.FullName}";
        }

        /// <summary>
        /// Gleicht die Einträge mit dem Manifest ab.
        /// </summary>
        /// <param name="entries">Die Einträge, wie das Archiv sie meldet.</param>
        /// <param name="manifest">Das gelesene und geprüfte Manifest.</param>
        /// <returns>Der Grund der Ablehnung, oder <see langword="null"/>.</returns>
        public static string? CheckAgainstManifest(IReadOnlyList<ArchiveEntryInfo> entries, LibraryManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(manifest);

            Dictionary<string, long> expected = new(StringComparer.Ordinal)
            {
                [LibraryArchiveFormat.DataPackageEntry] = manifest.DataPackage.Length,
            };
            foreach (LibraryFile file in manifest.Files)
            {
                expected[LibraryArchiveFormat.FileEntryName(file.Path)] = file.Length;
            }

            foreach (string directory in manifest.Directories)
            {
                expected[LibraryArchiveFormat.DirectoryEntryName(directory)] = 0;
            }

            HashSet<string> seen = new(StringComparer.Ordinal) { LibraryArchiveFormat.ManifestEntry };
            foreach (ArchiveEntryInfo entry in entries.Where(e => e.FullName != LibraryArchiveFormat.ManifestEntry))
            {
                string? problem = CheckEntry(entry, expected, seen);
                if (problem is not null)
                {
                    return problem;
                }
            }

            return seen.Count == expected.Count + 1 ? null : "Das Archiv enthält nicht alle Einträge des Manifests.";
        }

        private static string? CheckEntry(ArchiveEntryInfo entry, Dictionary<string, long> expected, HashSet<string> seen)
        {
            if (!expected.TryGetValue(entry.FullName, out long length))
            {
                return $"Eintrag steht nicht im Manifest: {entry.FullName}";
            }

            if (!seen.Add(entry.FullName))
            {
                return $"Eintrag doppelt: {entry.FullName}";
            }

            if (entry.Length != length || entry.CompressedLength < 0)
            {
                return $"Eintrag {entry.FullName} hat nicht die Länge aus dem Manifest.";
            }

            bool implausible = entry.Length > RatioThreshold
                && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > PackageFormat.MaxCompressionRatio);
            return implausible ? $"Eintrag {entry.FullName} wächst beim Entpacken unplausibel stark." : null;
        }

        private static bool HasKnownForm(string name)
        {
            if (name is LibraryArchiveFormat.ManifestEntry or LibraryArchiveFormat.DataPackageEntry)
            {
                return true;
            }

            if (!name.StartsWith(LibraryArchiveFormat.MediaPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string relative = name[LibraryArchiveFormat.MediaPrefix.Length..];
            return LibraryRelativePath.IsSafe(relative.EndsWith('/') ? relative[..^1] : relative);
        }
    }
}
