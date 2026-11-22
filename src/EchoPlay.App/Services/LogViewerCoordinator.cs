using EchoPlay.App.Models;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standardimplementierung von <see cref="ILogViewerCoordinator"/>.
    /// Nutzt den <see cref="LoggerManager"/> um das Log-Verzeichnis zu bestimmen und den
    /// optionalen <see cref="MemorySink"/>, um Live-Einträge zu liefern.
    /// Ist kein MemorySink registriert, bleibt die Live-Ansicht leer. Die Datei-Funktionen
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
            _memorySink = memorySink;

            // Coordinator und Puffer leben beide als Singleton bis zum Ende der Anwendung; ein
            // Abmelden hätte keinen Zeitpunkt, an dem es etwas freigäbe.
            if (_memorySink is not null)
            {
                _memorySink.LogEntryAdded += OnLogEntryAdded;
            }
        }

        /// <inheritdoc />
        public event EventHandler? LiveEntriesChanged;

        /// <inheritdoc />
        public bool IsLiveViewAvailable => _memorySink is not null;

        /// <inheritdoc />
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<LogFileOption>> LoadLogFileOptionsAsync(CancellationToken cancellationToken = default)
        {
            // Ein relativer Protokollordner gilt gegenüber dem Arbeitsverzeichnis; die Liste
            // trägt deshalb volle Pfade, die auch nach einem Wechsel des Verzeichnisses stimmen.
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
                    string fileName = Path.GetFileName(path);
                    DateTime lastWrite = File.GetLastWriteTime(path);
                    result.Add(new LogFileOption(fileName, lastWrite, path));
                }

                // Absteigend sortieren, die neueste Datei steht oben in der ComboBox
                result.Sort((a, b) => b.Date.CompareTo(a.Date));
                return result;
            }, cancellationToken);

            // Live-Option immer an erster Stelle, damit sie der Default bleibt. Ihr Name steht in
            // der Auswahlliste und kommt deshalb aus den Ressourcen.
            string liveLabel = EchoPlay.App.Helpers.SafeResourceLoader.Get("LogViewerLiveOption", "Aktuell (Live)");
            List<LogFileOption> allOptions = [new LogFileOption(liveLabel, DateTime.MaxValue, null), .. fileOptions];
            return allOptions;
        }

        /// <inheritdoc />
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="filePath">Vollständiger Pfad der zu lesenden Protokolldatei.</param>
        public async Task<IReadOnlyList<string>> LoadFileLinesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(filePath, System.Text.Encoding.UTF8, cancellationToken);
                return lines;
            }
            catch (IOException)
            {
                // Datei möglicherweise gerade gesperrt: stilles Ignorieren, leere Liste zurückgeben
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }

        /// <inheritdoc />
        /// <param name="searchText">Freitextfilter; leer zeigt alle Einträge.</param>
        /// <param name="minimumLevel">Untere Grenze der angezeigten Protokollstufe.</param>
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
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Fatal => "FATAL",
                _ => "?????"
            };

            // LogEntry.Timestamp ist UTC; Lokalzeit für den Anwender im Log-Viewer.
            string time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = $"{time} [{levelTag}] {entry.Category}: {entry.Message}";

            if (entry.Exception is not null)
            {
                line += $"  ({entry.Exception.GetType().Name}: {entry.Exception.Message})";
            }

            return line;
        }

        private void OnLogEntryAdded(object? sender, LogEntryEventArgs e)
        {
            LiveEntriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
