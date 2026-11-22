namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Was über einen Eintrag eines Archivs bekannt ist, bevor er entpackt wird.
    /// </summary>
    /// <param name="FullName">Der vollständige Name im Archiv.</param>
    /// <param name="CompressedLength">Gepackte Länge in Byte.</param>
    /// <param name="Length">Entpackte Länge in Byte, wie das Archiv sie angibt.</param>
    public sealed record ArchiveEntryInfo(string FullName, long CompressedLength, long Length);

    /// <summary>
    /// Prüft ein Archiv, bevor ein Byte daraus geschrieben wird.
    /// </summary>
    /// <remarks>
    /// Ein Paket kommt von außen und wird behandelt wie jede fremde Datei. Erlaubt sind genau die
    /// Einträge aus <see cref="PackageFormat"/>, ohne Verzeichnisanteil, jeder höchstens einmal. Damit
    /// kann kein Eintrag auf einen Pfad außerhalb zeigen (Zip-Slip). Die Längen und das Verhältnis
    /// entpackt zu gepackt fangen Archivbomben ab. Die entpackte Länge aus dem Archiv ist nur eine
    /// Angabe; das Entpacken zählt deshalb selbst mit und bricht ab, wenn sie überschritten wird.
    /// </remarks>
    public static class ArchiveGuard
    {
        /// <summary>
        /// Prüft die Einträge eines Archivs.
        /// </summary>
        /// <param name="entries">Die Einträge, wie das Archiv sie meldet.</param>
        /// <returns>Der Grund der Ablehnung, oder <see langword="null"/>, wenn das Archiv in Ordnung ist.</returns>
        public static string? Check(IReadOnlyList<ArchiveEntryInfo> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0 || entries.Count > PackageFormat.MaxEntries)
            {
                return $"Das Paket hat {entries.Count} Einträge, erwartet sind höchstens {PackageFormat.MaxEntries}.";
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (ArchiveEntryInfo entry in entries)
            {
                string? problem = CheckEntry(entry);
                if (problem is not null)
                {
                    return problem;
                }

                if (!seen.Add(entry.FullName))
                {
                    return $"Eintrag doppelt: {entry.FullName}";
                }
            }

            return seen.Contains(PackageFormat.ManifestEntry) && seen.Contains(PackageFormat.DatabaseEntry)
                ? null
                : "Manifest oder Datenbank fehlen im Paket.";
        }

        /// <summary>Die Höchstlänge, auf die ein Eintrag beim Entpacken wachsen darf.</summary>
        /// <param name="name">Name des Eintrags.</param>
        /// <returns>Die Grenze in Byte.</returns>
        public static long MaxLengthOf(string name) => name switch
        {
            PackageFormat.ManifestEntry => PackageFormat.MaxManifestLength * 4L,
            PackageFormat.CredentialsEntry => PackageFormat.MaxCredentialsLength,
            _ => PackageFormat.MaxDatabaseLength,
        };

        private static string? CheckEntry(ArchiveEntryInfo entry)
        {
            string name = entry.FullName;
            if (name != PackageFormat.ManifestEntry && !PackageFormat.IsContentEntry(name))
            {
                return $"Unerwarteter Eintrag: {name}";
            }

            if (entry.Length < 0 || entry.CompressedLength < 0 || entry.Length > MaxLengthOf(name))
            {
                return $"Eintrag {name} ist zu groß.";
            }

            const long OneMegabyte = 1024 * 1024;
            if (entry.Length > OneMegabyte
                && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > PackageFormat.MaxCompressionRatio))
            {
                return $"Eintrag {name} wächst beim Entpacken unplausibel stark.";
            }

            return null;
        }
    }
}
