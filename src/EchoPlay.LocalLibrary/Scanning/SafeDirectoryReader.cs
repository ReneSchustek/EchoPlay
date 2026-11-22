using EchoPlay.Core.Logging;
using EchoPlay.Logger.Abstractions;
using System.Security;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Liest Ordner und Audiodateien für den Scan. Ein unlesbarer Ordner ist kein Abbruchgrund:
    /// Er wird protokolliert und als leer behandelt, der Scan geht weiter.
    /// </summary>
    /// <param name="logger">Protokoll des Scans.</param>
    internal sealed class SafeDirectoryReader(ILogger logger)
    {
        /// <summary>
        /// Zählt alle unterstützten Audiodateien im angegebenen Verzeichnis rekursiv.
        /// Gibt 0 zurück, wenn die Zählung scheitert; der Fortschritt bleibt dann unbestimmt.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis.</param>
        /// <returns>Anzahl der Audiodateien oder 0 bei Fehler.</returns>
        public int CountAudioFiles(string rootPath)
        {
            // Ein gesperrter Unterordner, etwa „System Volume Information" an der Wurzel eines
            // Laufwerks, ließe die ganze Zählung scheitern; er wird übersprungen wie beim Einlesen.
            // Versteckte Dateien zählen mit, weil auch das Einlesen sie aufnimmt.
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None
            };

            try
            {
                return Directory.EnumerateFiles(rootPath, "*", options)
                    .Count(EchoPlay.Core.AudioExtensions.IsAudioFile);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or PathTooLongException
                                       or DirectoryNotFoundException
                                       or ArgumentException)
            {
                logger.Warning("Vorab-Zählung fehlgeschlagen, Fortschritt ohne Prozentangabe: {Reason}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Sammelt die Audiodateien einer Folge nach <see cref="EpisodeAudioFiles.Collect"/>;
        /// ein unlesbarer Ordner ergibt eine leere Liste und eine Warnung.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum Episodenordner.</param>
        /// <returns>Sortierte Liste der absoluten Dateipfade.</returns>
        public List<string> CollectTrackPaths(string folderPath)
        {
            try
            {
                return [.. EpisodeAudioFiles.Collect(folderPath)];
            }
            catch (IOException ex)
            {
                logger.Warning("Audiodateien konnten nicht gelesen werden in: {Path} – {Reason}", PathRedactor.Redact(folderPath), ex.Message);
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Warning("Zugriff verweigert für: {Path} – {Reason}", PathRedactor.Redact(folderPath), ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Liest alle direkten Unterordner des angegebenen Verzeichnisses.
        /// Bei IO-Fehlern wird ein leeres Array zurückgegeben und der Fehler geloggt.
        /// </summary>
        /// <param name="path">Absoluter Pfad zum Verzeichnis.</param>
        /// <returns>Array der Unterordnerpfade oder leeres Array bei Fehler.</returns>
        public string[] GetSubdirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (IOException ex)
            {
                logger.Warning("Unterordner konnten nicht gelesen werden: {Path} – {Reason}", PathRedactor.Redact(path), ex.Message);
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Warning("Zugriff verweigert für: {Path} – {Reason}", PathRedactor.Redact(path), ex.Message);
                return [];
            }
        }
    }
}
