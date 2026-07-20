using System.IO;

namespace EchoPlay.Core
{
    /// <summary>
    /// Bereinigt einzelne Datei-/Ordnernamen-Segmente von ungültigen Zeichen.
    /// Wird sowohl beim Umbenennen (TagManager) als auch beim Umstrukturieren
    /// (LocalLibrary) genutzt, damit beide dieselbe Regel anwenden.
    /// </summary>
    public static class PathSanitizer
    {
        /// <summary>
        /// Ersetzt alle für Datei-/Ordnernamen ungültigen Zeichen durch Unterstriche
        /// und entfernt führende sowie abschließende Leerzeichen.
        /// </summary>
        /// <param name="name">Das zu bereinigende Segment (Datei- oder Ordnername ohne Pfad).</param>
        /// <returns>Das bereinigte Segment.</returns>
        public static string SanitizeSegment(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name.Trim();
        }
    }
}
