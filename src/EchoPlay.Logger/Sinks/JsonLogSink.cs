using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Schreibt Log-Einträge als JSON-Lines (ein JSON-Objekt pro Zeile) in eine Datei.
    /// Für strukturiertes Log-Shipping an externe Tools. Felder: timestamp (ISO-8601 UTC),
    /// level, category, scopes (Array), message, exception (falls vorhanden).
    /// </summary>
    public sealed class JsonLogSink : ILogSink, IDisposable
    {
        private readonly string _logDirectory;
        private readonly long _maxFileSizeBytes;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        // UnsafeRelaxedJsonEscaping schreibt UTF-8-Klartext statt Unicode-Escapes für <, >, & und +.
        // Bewusste Wahl: lokale Logfiles bleiben mit Umlauten und Sonderzeichen lesbar. Real-Risiko
        // gering, weil Logs nicht in Web-Kontext gerendert werden. Falls die Logs künftig in eine
        // Web-UI oder externe Aggregation fließen, ist die Encoder-Wahl neu zu bewerten.
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Erstellt einen neuen JsonLogSink.
        /// </summary>
        /// <param name="logDirectory">Verzeichnis für JSON-Log-Dateien.</param>
        /// <param name="maxFileSizeMb">Maximale Dateigröße in MB pro JSON-Log (Standard: 10).</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sink-Konstruktor darf bei Verzeichnis-Fehlern nicht crashen; Fallback auf AppData\\EchoPlay\\Logs-Json analog FileSink, sonst No-Op.")]
        public JsonLogSink(string logDirectory, int maxFileSizeMb = 10)
        {
            _logDirectory = logDirectory;
            _maxFileSizeBytes = maxFileSizeMb * 1024L * 1024L;

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    _ = Directory.CreateDirectory(_logDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"JsonLogSink: Konnte Verzeichnis nicht erstellen: {ex.Message}");
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EchoPlay", "Logs-Json");
                try
                {
                    _ = Directory.CreateDirectory(fallback);
                    _logDirectory = fallback;
                }
                catch (Exception)
                {
                    _logDirectory = string.Empty;
                }
            }
        }

        /// <summary>
        /// Schreibt einen Eintrag als JSON-Zeile in die aktuelle Tagesdatei.
        /// </summary>
        /// <param name="entry">Der Log-Eintrag.</param>
        /// <returns>Task der asynchronen Operation.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sink darf durch IO-/Serialisierungs-Fehler die Anwendung nicht crashen; Fehler geht in Diagnostics-Trace.")]
        public async Task WriteAsync(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (string.IsNullOrEmpty(_logDirectory) || _disposed)
            {
                return;
            }

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string filePath = GetCurrentFilePath();
                string jsonLine = Serialize(entry);
                await File.AppendAllTextAsync(filePath, jsonLine + Environment.NewLine).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"JsonLogSink: Schreiben fehlgeschlagen: {ex.Message}");
            }
            finally
            {
                _ = _writeLock.Release();
            }
        }

        /// <summary>
        /// Serialisiert einen <see cref="LogEntry"/> in eine JSON-Zeile. Oeffentlich, damit Tests
        /// den Roundtrip ohne Datei-IO prüfen können.
        /// </summary>
        /// <param name="entry">Der Log-Eintrag.</param>
        /// <returns>JSON-Objekt als Single-Line-String.</returns>
        public static string Serialize(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            JsonLogRecord record = new(
                Timestamp: entry.Timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Level: entry.Level.ToString(),
                Category: entry.Category,
                Scopes: entry.Scopes,
                Message: entry.Message,
                Exception: entry.Exception is null
                    ? null
                    : new JsonExceptionRecord(
                        entry.Exception.GetType().FullName ?? entry.Exception.GetType().Name,
                        entry.Exception.Message,
                        entry.Exception.StackTrace));

            return JsonSerializer.Serialize(record, SerializerOptions);
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

        private string GetCurrentFilePath()
        {
            // Lokalzeit für Datei-Namen analog FileSink — Anwender ordnet die Datei seinem Tag zu.
            string baseName = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string filePath = Path.Combine(_logDirectory, $"{baseName}.jsonl");

            if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes) return filePath;

            int counter = 1;
            while (counter < 1000)
            {
                filePath = Path.Combine(_logDirectory, $"{baseName}_{counter:D3}.jsonl");
                if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes) return filePath;
                counter++;
            }
            return filePath;
        }

        private sealed record JsonLogRecord(
            string Timestamp,
            string Level,
            string Category,
            IReadOnlyList<string> Scopes,
            string Message,
            JsonExceptionRecord? Exception);

        private sealed record JsonExceptionRecord(
            string Type,
            string Message,
            string? StackTrace);
    }
}
