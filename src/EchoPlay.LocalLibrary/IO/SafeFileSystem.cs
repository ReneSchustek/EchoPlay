using System.IO;

namespace EchoPlay.LocalLibrary.IO
{
    /// <summary>
    /// Fehlertolerante Verzeichnis-Enumeration: liefert bei IO- oder Zugriffsfehlern ein
    /// leeres Array statt einer Ausnahme. Nur für Stellen gedacht, an denen ein Fehler
    /// semantisch einem leeren Verzeichnis entspricht (keine Diagnose-Warnung nötig).
    /// </summary>
    internal static class SafeFileSystem
    {
        /// <summary>
        /// Liefert die direkten Unterordner eines Verzeichnisses; leeres Array bei Fehler.
        /// </summary>
        /// <param name="path">Absoluter Pfad zum Verzeichnis.</param>
        /// <returns>Die Unterordnerpfade oder ein leeres Array.</returns>
        public static string[] EnumerateDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (IOException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }

        /// <summary>
        /// Liefert die direkten Dateien eines Verzeichnisses; leeres Array bei Fehler.
        /// </summary>
        /// <param name="path">Absoluter Pfad zum Verzeichnis.</param>
        /// <returns>Die Dateipfade oder ein leeres Array.</returns>
        public static string[] EnumerateFiles(string path)
        {
            try
            {
                return Directory.GetFiles(path);
            }
            catch (IOException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }
    }
}
