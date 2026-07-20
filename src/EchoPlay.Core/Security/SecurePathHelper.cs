using System;
using System.IO;

namespace EchoPlay.Core.Security
{
    /// <summary>
    /// Pfad-Sicherheitsprüfungen gegen Traversal-Angriffe und Symlink-Escape.
    /// Die App schreibt Cover- und Tag-Dateien in Verzeichnisse, deren Pfad
    /// aus DB-Werten oder Tag-Editor-Eingaben stammt — eine zentrale Helferklasse
    /// vermeidet, dass jede Schreibstelle eigene (oft unvollständige) Canonicalization implementiert.
    /// </summary>
    public static class SecurePathHelper
    {
        /// <summary>
        /// Prüft, ob <paramref name="candidate"/> tatsächlich innerhalb von
        /// <paramref name="root"/> liegt — auch nach Auflösung relativer Anteile (<c>..</c>),
        /// Symlinks und Groß-/Kleinschreibung.
        /// </summary>
        /// <param name="candidate">Zu prüfender Pfad (Datei oder Verzeichnis).</param>
        /// <param name="root">Wurzelverzeichnis, das <paramref name="candidate"/> einschließen muss.</param>
        /// <returns>
        /// <see langword="true"/>, wenn der kanonische Pfad von <paramref name="candidate"/>
        /// mit dem kanonischen Pfad von <paramref name="root"/> beginnt; sonst <see langword="false"/>.
        /// Liefert <see langword="false"/> bei <see langword="null"/>/leeren Eingaben oder wenn
        /// die Auflösung scheitert (z. B. ungültige Pfadzeichen).
        /// </returns>
        public static bool IsPathInside(string? candidate, string? root)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string? canonicalCandidate = TryGetFullPath(candidate);
            string? canonicalRoot = TryGetFullPath(root);

            if (canonicalCandidate is null || canonicalRoot is null)
            {
                return false;
            }

            // Trailing-Separator garantiert, dass "/foo" nicht als Prefix von "/foobar" durchgeht.
            string normalizedRoot = EnsureTrailingSeparator(canonicalRoot);
            string comparedCandidate = EnsureTrailingSeparator(canonicalCandidate);

            return comparedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        // Pfad in absolute Form bringen; bei ungültigen Zeichen oder zu langen Pfaden
        // gibt .NET ArgumentException/PathTooLongException — die schlucken wir hier
        // und melden Failure, damit Aufrufer sicher 'false' zurückbekommen.
        private static string? TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
    }
}
