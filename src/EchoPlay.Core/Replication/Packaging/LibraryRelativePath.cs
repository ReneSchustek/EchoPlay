using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Relative Pfade im Mediathek-Archiv: wie sie gebildet werden und welche überhaupt gelten.
    /// </summary>
    /// <remarks>
    /// Ein Pfad aus einem fremden Archiv darf auf dem Zielrechner nur unter dem gewählten Ordner
    /// landen. Abgelehnt wird deshalb nicht nur <c>..</c>, sondern alles, was Windows beim Anlegen
    /// umdeutet: ein Laufwerk oder Datenstrom (<c>:</c>), Gerätenamen wie <c>NUL</c>, ein Punkt oder
    /// Leerzeichen am Ende eines Abschnitts (Windows schneidet es ab, zwei Namen fielen zusammen) und
    /// der Rückstrich, den Windows als Trenner liest. Trenner im Archiv ist immer <c>/</c>.
    /// </remarks>
    public static class LibraryRelativePath
    {
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // Fest statt Path.GetInvalidFileNameChars(): Die Liste hängt dort am Betriebssystem, das
        // Archiv soll aber auf jedem Rechner gleich geprüft werden.
        private static readonly System.Buffers.SearchValues<char> InvalidCharacters = System.Buffers.SearchValues.Create("<>:\"\\|?*");

        /// <summary>
        /// Ob ein relativer Pfad aus einem Archiv sicher unter einem Ordner angelegt werden kann.
        /// </summary>
        /// <param name="path">Der Pfad mit <c>/</c> als Trenner.</param>
        /// <returns><see langword="true"/>, wenn jeder Abschnitt ein gewöhnlicher Name ist.</returns>
        public static bool IsSafe([NotNullWhen(true)] string? path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > LibraryArchiveFormat.MaxRelativePathLength)
            {
                return false;
            }

            foreach (string segment in path.Split('/'))
            {
                if (!IsSafeSegment(segment))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Bildet den relativen Pfad einer Datei oder eines Ordners unter dem Stammpfad.
        /// </summary>
        /// <param name="root">Der Stammpfad der Mediathek.</param>
        /// <param name="fullPath">Der absolute Pfad darunter.</param>
        /// <returns>Der relative Pfad mit <c>/</c> als Trenner.</returns>
        /// <exception cref="ArgumentException">Der Pfad liegt nicht unter dem Stammpfad.</exception>
        public static string FromFullPath(string root, string fullPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

            // Abschneiden statt Path.GetRelativePath: Das normalisiert und striche einen Punkt am
            // Namensende weg; genau den soll IsSafe danach aber sehen und abweisen.
            string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || fullPath.Length == prefix.Length)
            {
                throw new ArgumentException($"{fullPath} liegt nicht unter {root}.", nameof(fullPath));
            }

            return fullPath[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Löst einen geprüften relativen Pfad unter einem Ordner auf und stellt sicher, dass das
        /// Ergebnis wirklich darunter liegt.
        /// </summary>
        /// <param name="root">Der Zielordner, absolut.</param>
        /// <param name="relativePath">Der relative Pfad aus dem Archiv.</param>
        /// <returns>Der absolute Pfad.</returns>
        /// <exception cref="ArgumentException">Der Pfad ist nicht sicher oder führte aus dem Ordner hinaus.</exception>
        public static string Resolve(string root, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            if (!IsSafe(relativePath))
            {
                throw new ArgumentException($"Unzulässiger Pfad im Archiv: {relativePath}", nameof(relativePath));
            }

            string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            // Die Prüfung oben schließt das schon aus; diese zweite Sperre sitzt an der Stelle, an der
            // der Pfad tatsächlich entsteht, und hält auch dann, wenn jemand die erste lockert.
            return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full
                : throw new ArgumentException($"Der Pfad führt aus dem Zielordner hinaus: {relativePath}", nameof(relativePath));
        }

        private static bool IsSafeSegment(string segment)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }

            if (segment[^1] is '.' or ' ' || segment.AsSpan().ContainsAny(InvalidCharacters) || segment.Any(char.IsControl))
            {
                return false;
            }

            // NUL und NUL.txt sind beide das Gerät; maßgeblich ist der Teil vor dem ersten Punkt.
            int dot = segment.IndexOf('.', StringComparison.Ordinal);
            return !ReservedNames.Contains(dot < 0 ? segment : segment[..dot]);
        }
    }
}
