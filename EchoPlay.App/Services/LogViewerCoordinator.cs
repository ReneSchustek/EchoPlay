using EchoPlay.App.Models;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standardimplementierung von <see cref="ILogViewerCoordinator"/>.
    /// Nutzt den <see cref="LoggerManager"/> um das Log-Verzeichnis zu bestimmen und den
    /// optionalen <see cref="MemorySink"/>, um Live-Einträge zu liefern.
    /// Ist kein MemorySink registriert, bleibt die Live-Ansicht leer – die Datei-Funktionen
    /// arbeiten davon unabhängig weiter.
    /// </summary>
    public sealed class LogViewerCoordinator : ILogViewerCoordinator
    {
        private readonly LoggerManager _loggerManager;
        private readonly MemorySink? _memorySink;

        /// <summary>
        /// Initialisiert den Coordinator mit dem Logger-Manager und optionalem MemorySink.
        /// </summary>
        /// <param name="loggerManager">Für das Log-Verzeichnis.</param>
        /// <param name="memorySink">Optionaler In-Memory-Puffer für die Live-Ansicht.</param>
        public LogViewerCoordinator(LoggerManager loggerManager, MemorySink? memorySink = null)
        {
            _loggerManager = loggerManager;
            _memorySink    = memorySink;
        }

        /// <inheritdoc />
        public bool IsLiveViewAvailable => _memorySink is not null;

        /// <inheritdoc />
        public async Task<IReadOnlyList<LogFileOption>> LoadLogFileOptionsAsync()
        {
            // Relativen Pfad in absoluten umwandeln, da AppContext.BaseDirectory je nach Start-Kontext variiert
            string logDirectory = _loggerManager.LogDirectory;
            if (!Path.IsPathRooted(logDirectory))
            {
                logDirectory = Path.GetFullPath(logDirectory);
            }

            // Dateisystem-Zugriff in Thread-Pool auslagern, um den UI-Thread nicht zu blockieren
            List<LogFileOption> fileOptions = await Task.Run(() =>
            {
                List<LogFileOption> result = [];

                if (!Directory.Exists(logDirectory))
                {
                    return result;
                }

                foreach (string path in Directory.GetFiles(logDirectory, "*.log"))
                {
                    string fileName    = Path.GetFileName(path);
                    DateTime lastWrite = File.GetLastWriteTime(path);
                    result.Add(new LogFileOption(fileName, lastWrite, path));
                }

                // Absteigend sortieren – neueste Datei steht oben in der ComboBox
                result.Sort((a, b) => b.Date.CompareTo(a.Date));
                return result;
            });

            // Live-Option immer an erster Stelle, damit sie der Default bleibt
            List<LogFileOption> allOptions = [new LogFileOption("Aktuell (Live)", DateTime.MaxValue, null), .. fileOptions];
            return allOptions;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> LoadFileLinesAsync(string filePath)
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(filePath, System.Text.Encoding.UTF8);
                return lines;
            }
            catch (IOException)
            {
                // Datei möglicherweise gerade gesperrt – stilles Ignorieren, leere Liste zurückgeben
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> BuildFilteredLiveEntries(string searchText, LogLevel minimumLevel)
        {
            if (_memorySink is null)
            {
                return [];
            }

            IReadOnlyList<LogEntry> entries = _memorySink.GetEntries();
            List<string> result = new(entries.Count);

            foreach (LogEntry entry in entries)
            {
                // Level-Filter: Einträge unterhalb des Mindest-Levels werden übersprungen
                if (entry.Level < minimumLevel)
                {
                    continue;
                }

                string line = FormatLogEntry(entry);

                // Suchfilter: Groß-/Kleinschreibung ignorieren
                if (!string.IsNullOrWhiteSpace(searchText)
                    && !line.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(line);
            }

            return result;
        }

        /// <summary>
        /// Formatiert einen <see cref="LogEntry"/> für die einzeilige Darstellung im Log-Viewer.
        /// Format: <c>HH:mm:ss [LEVEL] Kategorie: Nachricht (ExceptionType: ExceptionMessage)</c>.
        /// </summary>
        /// <param name="entry">Der zu formatierende Eintrag.</param>
        /// <returns>Formatierter Ein-Zeiler.</returns>
        private static string FormatLogEntry(LogEntry entry)
        {
            string levelTag = entry.Level switch
            {
                LogLevel.Trace       => "TRACE",
                LogLevel.Debug       => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning     => "WARN ",
                LogLevel.Error       => "ERROR",
                LogLevel.Fatal       => "FATAL",
                _                    => "????? "
            };

            string time = entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = $"{time} [{levelTag}] {entry.Category}: {entry.Message}";

            if (entry.Exception is not null)
            {
                line += $"  ({entry.Exception.GetType().Name}: {entry.Exception.Message})";
            }

            return line;
        }
    }
}
