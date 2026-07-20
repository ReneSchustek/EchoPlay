using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EchoPlay.Core.Logging
{
    /// <summary>
    /// Reduziert vollständige Datei- und Verzeichnis-Pfade auf eine logging-sichere Form,
    /// damit der Username und tiefer Verzeichnisbaum in Crash-Reports und Log-Files
    /// nicht im Klartext erscheinen. Der Datei-Name bleibt sichtbar (Diagnostic-Wert),
    /// das Verzeichnis wird durch einen 8-Zeichen-Hash ersetzt — gleiche Verzeichnisse
    /// erhalten denselben Hash, sodass Korrelation in einer Log-Sequenz möglich bleibt.
    /// </summary>
    public static class PathRedactor
    {
        private const string EmptyPlaceholder = "(kein Pfad)";

        /// <summary>
        /// Maskiert <paramref name="fullPath"/> für die Logger-Ausgabe:
        /// Verzeichnis-Anteil als 8-Hex-Zeichen-SHA-256-Hash, Datei-/Ordnername bleibt sichtbar.
        /// Liefert <c>"(kein Pfad)"</c> bei <see langword="null"/>/leer.
        /// </summary>
        /// <example>
        /// <c>C:\Users\Renes\Music\TKKG\Folge42.mp3</c> → <c>...\a3f7c1d8\Folge42.mp3</c>
        /// </example>
        public static string Redact(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return EmptyPlaceholder;
            }

            string fileName = Path.GetFileName(fullPath);
            string? directory = Path.GetDirectoryName(fullPath);

            if (string.IsNullOrEmpty(directory))
            {
                // Reine Pfad-Trennzeichen ("\\", "/") liefern leeren Datei-Namen — Fallback.
                return string.IsNullOrEmpty(fileName) ? EmptyPlaceholder : fileName;
            }

            string directoryHash = ComputeShortHash(directory);
            return $"...\\{directoryHash}\\{fileName}";
        }

        // SHA-256 mit OrdinalIgnoreCase auf normalisiertem Pfad: gleiche Verzeichnisse
        // (auch mit unterschiedlicher Schreibweise) erhalten denselben Hash, damit
        // Log-Korrelation möglich bleibt.
        private static string ComputeShortHash(string directory)
        {
            string normalized = directory.ToUpperInvariant();
            byte[] bytes = Encoding.UTF8.GetBytes(normalized);
            byte[] hash = SHA256.HashData(bytes);

            StringBuilder builder = new(8);
            for (int i = 0; i < 4; i++)
            {
                _ = builder.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
