using System;
using System.IO;

namespace EchoPlay.Core.Security
{
    /// <summary>
    /// Pfad-Sicherheitsprüfungen gegen Traversal-Angriffe.
    /// Die App schreibt Cover- und Tag-Dateien in Verzeichnisse, deren Pfad
    /// aus DB-Werten oder Tag-Editor-Eingaben stammt; eine zentrale Helferklasse
    /// vermeidet, dass jede Schreibstelle eigene (oft unvollständige) Canonicalization implementiert.
    /// </summary>
    public static class SecurePathHelper
    {
        /// <summary>
        /// Prüft, ob <paramref name="candidate"/> tatsächlich innerhalb von
        /// <paramref name="root"/> liegt, auch nach Auflösung relativer Anteile (<c>..</c>) und
        /// ohne Rücksicht auf Groß-/Kleinschreibung.
        /// </summary>
        /// <remarks>
        /// Symbolische Verknüpfungen und Verbindungspunkte werden nicht aufgelöst:
        /// <see cref="Path.GetFullPath(string)"/> arbeitet nur am Text des Pfads. Ein Ordner
        /// unter <paramref name="root"/>, der auf ein Ziel außerhalb zeigt, gilt deshalb als innen.
        /// </remarks>
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
        // wirft .NET ArgumentException oder PathTooLongException. Beide werden hier gefangen
        // und als Failure gemeldet, damit der Aufrufer sicher 'false' bekommt.
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
