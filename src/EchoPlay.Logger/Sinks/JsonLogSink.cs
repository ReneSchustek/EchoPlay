using System.Globalization;
using System.Text.Json;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Schreibt Log-Einträge als JSON-Lines (ein JSON-Objekt pro Zeile) in eine Datei mit
    /// täglicher Rotation und Größenlimit. Für strukturiertes Log-Shipping an externe Tools.
    /// Felder: timestamp (ISO-8601 UTC), level, category, scopes (Array), message,
    /// exception (falls vorhanden).
    /// </summary>
    public sealed class JsonLogSink : RotatingFileSinkBase
    {
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
        public JsonLogSink(string logDirectory, int maxFileSizeMb = 10)
            : base(logDirectory, "Logs-Json", ".jsonl", maxFileSizeMb)
        {
        }

        /// <inheritdoc/>
        protected override string FormatLine(LogEntry entry) => Serialize(entry);

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
