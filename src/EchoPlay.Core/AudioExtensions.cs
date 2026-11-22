using System.Collections.Generic;

namespace EchoPlay.Core
{
    /// <summary>
    /// Zentrale Definition aller unterstützten Audio-Dateierweiterungen.
    /// Wird projektübergreifend verwendet: von Scanner, Player, Analyse und Fehlende-Folgen-Erkennung.
    /// Dadurch bleibt die Liste konsistent und muss nur an einer Stelle gepflegt werden.
    /// </summary>
    public static class AudioExtensions
    {
        /// <summary>
        /// Dateierweiterungen, die von Windows Media Foundation abgespielt werden können.
        /// Diese Liste bestimmt, welche Dateien als Audioinhalte erkannt und wiedergegeben werden.
        /// </summary>
        /// <remarks>
        /// Formate wie <c>.ape</c> oder <c>.mpc</c> fehlen, denn Windows Media Foundation
        /// unterstützt sie nicht nativ, und Codec-Packs sind auf Endnutzer-PCs nicht garantiert.
        /// TagLib# kann deren Metadaten trotzdem lesen (siehe <c>TagService</c>).
        /// </remarks>
        public static readonly IReadOnlyList<string> Supported =
        [
            ".mp3",
            ".m4a",
            ".flac",
            ".ogg",
            ".wma",
            ".wav",
            ".aac",
            ".opus"
        ];

        /// <summary>
        /// Prüft, ob der angegebene Dateipfad eine unterstützte Audioerweiterung hat.
        /// Der Vergleich ist case-insensitive.
        /// </summary>
        /// <param name="filePath">Dateiname oder vollständiger Pfad.</param>
        /// <returns><c>true</c> wenn die Erweiterung in <see cref="Supported"/> enthalten ist.</returns>
        public static bool IsAudioFile(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath);

            foreach (string supported in Supported)
            {
                if (string.Equals(ext, supported, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
