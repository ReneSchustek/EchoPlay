using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Basis für dateibasierte Log-Sinks mit täglicher Rotation und Größenlimit.
    /// Kapselt die Verzeichnis-Anlage samt AppData-Fallback, das thread-sichere Anhängen
    /// und die Rotationslogik. Ableitungen liefern nur die Dateiendung, den Fallback-Ordner
    /// und die Zeilenformatierung.
    /// </summary>
    public abstract class RotatingFileSinkBase : ILogSink, IDisposable
    {
        private readonly long _maxFileSizeBytes;
        private readonly string _fileExtension;
        // Parallele WriteAsync-Aufrufer würden sich auf File.AppendAllText gegenseitig blockieren
        // oder Zeilen verzahnen. Semaphore serialisiert den Datei-Zugriff prozessweit.
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        private string _logDirectory;

        /// <summary>
        /// Initialisiert die Basis: legt das Log-Verzeichnis an oder weicht bei Fehlern auf einen
        /// Ordner unterhalb von AppData aus. Ist auch der Fallback nicht beschreibbar, wird das
        /// Logging zu einem No-Op, damit die App auch ohne Logging startet.
        /// </summary>
        /// <param name="logDirectory">Gewünschtes Verzeichnis für Log-Dateien.</param>
        /// <param name="appDataFallbackFolder">Unterordner unter <c>AppData\EchoPlay</c> für den Fallback.</param>
        /// <param name="fileExtension">Dateiendung inkl. Punkt (z.B. <c>.log</c> oder <c>.jsonl</c>).</param>
        /// <param name="maxFileSizeMb">Maximale Dateigröße in MB vor Rotation.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sink-Konstruktor darf bei Verzeichnis-Fehlern nicht crashen; Fallback auf AppData\\EchoPlay, sonst No-Op, damit die App auch ohne Logging startet.")]
        protected RotatingFileSinkBase(string logDirectory, string appDataFallbackFolder, string fileExtension, int maxFileSizeMb)
        {
            _fileExtension = fileExtension;
            _maxFileSizeBytes = maxFileSizeMb * 1024L * 1024L;
            _logDirectory = logDirectory;

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    _ = Directory.CreateDirectory(_logDirectory);
                }
            }
            catch (Exception ex)
            {
                // Fallback bei Rechteproblem oder ungültigem Pfad: AppData\EchoPlay\<Ordner> nutzen.
                // Das aktuelle Verzeichnis ist kein tauglicher Fallback – in installierten Apps
                // liegt das oft unter C:\Program Files\, wo Schreibzugriff verweigert wird.
                System.Diagnostics.Trace.WriteLine($"{GetType().Name}: Konnte Verzeichnis nicht erstellen: {ex.Message}");

                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EchoPlay", appDataFallbackFolder);

                try
                {
                    _ = Directory.CreateDirectory(fallback);
                    _logDirectory = fallback;
                }
                catch (Exception)
                {
                    // Kein beschreibbares Verzeichnis verfügbar – Logging deaktiviert.
                    _logDirectory = string.Empty;
                }
            }
        }

        /// <summary>
        /// Formatiert einen Log-Eintrag zu genau einer Ausgabezeile.
        /// </summary>
        /// <param name="entry">Der zu formatierende Log-Eintrag.</param>
        /// <returns>Die zu schreibende Zeile (ohne Zeilenumbruch).</returns>
        protected abstract string FormatLine(LogEntry entry);

        /// <summary>
        /// Schreibt einen Log-Eintrag asynchron in die aktuelle Tagesdatei.
        /// </summary>
        /// <param name="entry">Der zu schreibende Log-Eintrag.</param>
        /// <returns>Ein Task, der die asynchrone Operation repräsentiert.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sink darf durch IO-/Serialisierungs-Fehler die Anwendung nicht crashen; catch-all mit Diagnostics-Trace ist Resilience-Pattern.")]
        public async Task WriteAsync(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            // Wenn der Konstruktor kein beschreibbares Verzeichnis finden konnte,
            // wird WriteAsync stillschweigend zu einem No-Op.
            if (string.IsNullOrEmpty(_logDirectory) || _disposed)
            {
                return;
            }

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string filePath = GetCurrentFilePath();
                string line = FormatLine(entry);

                await File.AppendAllTextAsync(filePath, line + Environment.NewLine).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Letzter Ausweg: Wenigstens in Debug-Konsole schreiben
                System.Diagnostics.Trace.WriteLine($"{GetType().Name}: Schreiben fehlgeschlagen: {ex.Message}");
            }
            finally
            {
                _ = _writeLock.Release();
            }
        }

        /// <summary>
        /// Gibt die Schreib-Semaphore frei.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gibt verwaltete Ressourcen frei.
        /// </summary>
        /// <param name="disposing"><c>true</c>, wenn aus <see cref="Dispose()"/> aufgerufen.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _writeLock.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// Ermittelt den Pfad zur aktuellen Log-Datei unter Berücksichtigung von täglicher
        /// Rotation und Größenlimit.
        /// </summary>
        /// <returns>Vollständiger Pfad zur zu beschreibenden Log-Datei.</returns>
        private string GetCurrentFilePath()
        {
            // Lokalzeit für Datei-Namen, damit der Anwender Log-Dateien anhand seiner Tageszeit
            // wiederfindet (UTC-Datums-Sprung um Mitternacht wäre irritierend).
            string baseName = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string filePath = Path.Combine(_logDirectory, $"{baseName}{_fileExtension}");

            // Hauptdatei existiert nicht oder hat noch Platz? → nutzen
            if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes)
            {
                return filePath;
            }

            // Hauptdatei voll → nächste freie Nummer suchen
            int counter = 1;
            while (counter < 1000) // Sicherheitslimit
            {
                filePath = Path.Combine(_logDirectory, $"{baseName}_{counter:D3}{_fileExtension}");

                if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes)
                {
                    return filePath;
                }

                counter++;
            }

            // Notfall: Mehr als 1000 Dateien an einem Tag? Letzte überschreiben.
            return filePath;
        }
    }
}
