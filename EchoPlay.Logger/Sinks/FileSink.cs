using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Schreibt Log-Einträge in Dateien mit täglicher Rotation und Größenlimit.
    /// </summary>
    public sealed class FileSink : ILogSink, IDisposable
    {
        private readonly string _logDirectory;
        private readonly ILogFormatter _formatter;
        private readonly long _maxFileSizeBytes;
        // Parallele WriteAsync-Aufrufer würden sich auf File.AppendAllText gegenseitig blockieren
        // oder Zeilen verzahnen. Semaphore serialisiert den Datei-Zugriff prozessweit.
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        /// <summary>
        /// Erstellt einen neuen FileSink.
        /// </summary>
        /// <param name="logDirectory">Verzeichnis für Log-Dateien.</param>
        /// <param name="formatter">Der Formatter für die Log-Ausgabe.</param>
        /// <param name="maxFileSizeMb">Maximale Dateigröße in MB (Standard: 10).</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "FileSink darf bei Verzeichnis-Fehlern nicht crashen; Fallback auf AppData\\EchoPlay\\Logs, sonst No-Op, damit die App auch ohne Logging startet.")]
        public FileSink(string logDirectory, ILogFormatter formatter, int maxFileSizeMb = 10)
        {
            _logDirectory = logDirectory;
            _formatter = formatter;
            _maxFileSizeBytes = maxFileSizeMb * 1024 * 1024;

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    _ = Directory.CreateDirectory(_logDirectory);
                }
            }
            catch (Exception ex)
            {
                // Fallback bei Rechteproblem oder ungültigem Pfad: AppData\EchoPlay\Logs nutzen.
                // Das aktuelle Verzeichnis ist kein tauglicher Fallback – in installierten Apps
                // liegt das oft unter C:\Program Files\, wo Schreibzugriff verweigert wird.
                System.Diagnostics.Trace.WriteLine($"FileSink: Konnte Verzeichnis nicht erstellen: {ex.Message}");

                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EchoPlay", "Logs");

                try
                {
                    _ = Directory.CreateDirectory(fallback);
                    _logDirectory = fallback;
                }
                catch (Exception)
                {
                    // Kein beschreibbares Verzeichnis verfügbar – Logging deaktiviert.
                    // Fehler werden in WriteAsync stillschweigend geloggt.
                    _logDirectory = string.Empty;
                }
            }
        }

        /// <summary>
        /// Schreibt einen Log-Eintrag asynchron in die Tagesdatei.
        /// </summary>
        /// <param name="entry">Der zu schreibende Log-Eintrag.</param>
        /// <returns>Ein Task der die asynchrone Operation repräsentiert.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sink darf durch IO-Fehler die Anwendung nicht crashen; catch-all mit Diagnostics-Trace ist Resilience-Pattern.")]
        public async Task WriteAsync(LogEntry entry)
        {
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
                string formattedMessage = _formatter.Format(entry);

                await File.AppendAllTextAsync(filePath, formattedMessage + Environment.NewLine).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Letzter Ausweg: Wenigstens in Debug-Konsole schreiben
                System.Diagnostics.Trace.WriteLine($"FileSink: Schreiben fehlgeschlagen: {ex.Message}");
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
            if (_disposed) return;
            _disposed = true;
            _writeLock.Dispose();
        }

        /// <summary>
        /// Ermittelt den Pfad zur aktuellen Log-Datei.
        /// Berücksichtigt tägliche Rotation und Größenlimit.
        /// </summary>
        /// <returns>Vollständiger Pfad zur Log-Datei.</returns>
        private string GetCurrentFilePath()
        {
            string baseName = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string filePath = Path.Combine(_logDirectory, $"{baseName}.log");

            // Hauptdatei existiert nicht oder hat noch Platz? → nutzen
            if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes) return filePath;

            // Hauptdatei voll → suche nächste freie Nummer
            int counter = 1;
            while (counter < 1000) // Sicherheitslimit
            {
                filePath = Path.Combine(_logDirectory, $"{baseName}_{counter:D3}.log");

                // Datei existiert nicht oder hat noch Platz? → nutzen
                if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes)  return filePath;

                counter++;
            }

            // Notfall: Mehr als 1000 Dateien an einem Tag? Überschreibe letzte.
            return filePath;
        }
    }
}