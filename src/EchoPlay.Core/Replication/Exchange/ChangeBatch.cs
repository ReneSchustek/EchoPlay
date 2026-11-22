using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>
    /// Eine Datei im Änderungsprotokoll eines Geräts: entweder die Änderungen seit der vorigen Datei
    /// oder ein Gesamtstand, der alle früheren Dateien dieses Geräts ersetzt.
    /// </summary>
    /// <remarks>
    /// Jede Datei wird einmal geschrieben und nie verändert. Die Nummer zählt je Gerät lückenlos
    /// hoch; ein Gesamtstand trägt die Nummer der letzten Änderung, die er einschließt. Das Format ist
    /// in <c>ABGLEICH.md</c> beschrieben.
    /// </remarks>
    /// <param name="FormatVersion">Version des Formats.</param>
    /// <param name="DeviceId">Das schreibende Gerät.</param>
    /// <param name="Sequence">Laufende Nummer der Datei bei diesem Gerät, ab 1.</param>
    /// <param name="IsSnapshot">Ob die Datei ein Gesamtstand ist.</param>
    /// <param name="Records">Die Sätze.</param>
    public sealed record ChangeBatch(int FormatVersion, Guid DeviceId, long Sequence, bool IsSnapshot, IReadOnlyList<ChangeRecord> Records)
    {
        /// <summary>Aktuelle Version des Formats.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Höchstlänge einer Datei in Zeichen; ein Gesamtstand des Produktivbestands hat gut 5 MB.</summary>
        public const int MaxLength = 256 * 1024 * 1024;

        /// <summary>Höchstzahl der Sätze in einer Datei.</summary>
        public const int MaxRecords = 1_000_000;

        /// <summary>Höchstlänge eines Schlüssels.</summary>
        public const int MaxKeyLength = 2048;

        /// <summary>Höchstlänge eines Feldnamens.</summary>
        public const int MaxFieldNameLength = 64;

        /// <summary>Höchstlänge eines Feldwerts in Zeichen.</summary>
        public const int MaxFieldValueLength = 64 * 1024;

        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        };

        /// <summary>Schreibt die Datei als JSON.</summary>
        /// <returns>Das JSON.</returns>
        public string ToJson() => JsonSerializer.Serialize(new BatchDocument(
            FormatVersion,
            DeviceId.ToString("N"),
            Sequence,
            IsSnapshot,
            [.. Records.Select(r => new RecordDocument(
                r.Kind, r.Key, r.Parent, r.Stamp.ToString(), r.Deleted,
                r.Fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal)))]), Options);

        /// <summary>
        /// Liest eine Datei streng. Eine Datei, die von außen kommt und abgeschnitten, beschädigt oder
        /// fremd sein kann, wird lieber abgelehnt als halb verstanden.
        /// </summary>
        /// <param name="json">Der Inhalt.</param>
        /// <param name="batch">Die gelesene Datei.</param>
        /// <param name="error">Der Grund der Ablehnung.</param>
        /// <returns><see langword="true"/>, wenn die Datei gültig ist.</returns>
        public static bool TryParse(string? json, [NotNullWhen(true)] out ChangeBatch? batch, out string? error)
        {
            batch = null;
            error = StrictJson.TryDeserialize(
                json, MaxLength, Options,
                "Datei leer oder zu groß.", "Kein gültiges JSON; die Datei ist abgeschnitten oder beschädigt.",
                out BatchDocument? document)
                ?? ValidateHeader(document);
            if (error is not null)
            {
                return false;
            }

            // ValidateHeader hat Kopf und Satzliste auf null geprüft.
            error = ValidateRecords(document!.Records!, out List<ChangeRecord> records);
            if (error is not null)
            {
                return false;
            }

            // ValidateHeader hat die Gerätekennung geprüft.
            batch = new ChangeBatch(document.FormatVersion, Guid.ParseExact(document.DeviceId!, "N"), document.Sequence, document.Snapshot, records);
            return true;
        }

        /// <summary>Prüft jede Zeile; die erste ungültige bricht ab.</summary>
        private static string? ValidateRecords(List<RecordDocument> documents, out List<ChangeRecord> records)
        {
            records = new(documents.Count);
            foreach (RecordDocument record in documents)
            {
                string? error = ValidateRecord(record, out ChangeRecord? parsed);
                if (error is not null)
                {
                    return error;
                }

                records.Add(parsed!); // ohne Fehler ist der Satz immer gesetzt
            }

            return null;
        }

        private static string? ValidateHeader(BatchDocument? d)
        {
            if (d is null)
            {
                return "Datei ist leer.";
            }

            if (d.FormatVersion != CurrentFormatVersion)
            {
                return $"Protokollformat {d.FormatVersion} wird nicht unterstützt.";
            }

            if (!Guid.TryParseExact(d.DeviceId, "N", out Guid device) || device == Guid.Empty)
            {
                return "Geräte-Kennung fehlt.";
            }

            if (d.Sequence < 1)
            {
                return "Laufende Nummer fehlt.";
            }

            return d.Records is null || d.Records.Count > MaxRecords ? "Sätze fehlen oder sind zu viele." : null;
        }

        private static string? ValidateRecord(RecordDocument r, out ChangeRecord? record)
        {
            record = null;
            if (!ChangeKinds.IsKnown(r.Kind))
            {
                return $"Unbekannte Art: {r.Kind}";
            }

            if (string.IsNullOrEmpty(r.Key) || r.Key.Length > MaxKeyLength || r.Parent is { Length: > MaxKeyLength })
            {
                return "Schlüssel fehlt oder ist zu lang.";
            }

            if (!HybridTimestamp.TryParse(r.Stamp, out HybridTimestamp stamp))
            {
                return $"Zeitpunkt von {r.Key} ist ungültig.";
            }

            if (r.Fields is null || r.Fields.Any(f => f.Key.Length is 0 or > MaxFieldNameLength || f.Value is { Length: > MaxFieldValueLength }))
            {
                return $"Felder von {r.Key} fehlen oder sind zu lang.";
            }

            record = new ChangeRecord(r.Kind, r.Key, string.IsNullOrEmpty(r.Parent) ? null : r.Parent, stamp, r.Deleted, new Dictionary<string, string?>(r.Fields, StringComparer.Ordinal));
            return null;
        }

        /// <summary>Die Datei, wie sie als JSON steht; jedes Feld darf fehlen und wird geprüft.</summary>
        private sealed record BatchDocument(int FormatVersion, string? DeviceId, long Sequence, bool Snapshot, List<RecordDocument>? Records);

        /// <summary>Ein Satz, wie er als JSON steht.</summary>
        private sealed record RecordDocument(string? Kind, string? Key, string? Parent, string? Stamp, bool Deleted, Dictionary<string, string?>? Fields);
    }
}
