using EchoPlay.App.Helpers;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Enthält die Statistiken eines abgeschlossenen Bibliothek-Sync-Vorgangs.
    /// </summary>
    public sealed class SyncResult
    {
        /// <summary>
        /// Lokale Serienordner, die einer Datenbank-Serie zugeordnet wurden.
        /// </summary>
        public int SeriesMatched { get; init; }

        /// <summary>
        /// Lokale Serienordner ohne Datenbank-Treffer (kein Fehler, nur gezählt).
        /// </summary>
        public int SeriesUnmatched { get; init; }

        /// <summary>
        /// Episoden, bei denen lokale Daten (Pfad, Trackanzahl, Matchart) aktualisiert wurden.
        /// </summary>
        public int EpisodesUpdated { get; init; }

        /// <summary>
        /// Neu angelegte <see cref="EchoPlay.Data.Entities.Library.LocalTrack"/>-Einträge.
        /// </summary>
        public int TracksCreated { get; init; }

        /// <summary>
        /// Gibt eine lesbare Zusammenfassung des Sync-Ergebnisses zurück.
        /// Wenn Ordner gefunden wurden, aber keine passende Serie in der Datenbank vorhanden ist,
        /// erscheint ein Hinweis, denn das ist der häufigste Grund für "0 Ergebnisse" nach einem Erst-Scan.
        /// </summary>
        /// <returns>Ergebnistext in der Sprache der Oberfläche; die Einstellungsseite zeigt ihn an.</returns>
        public override string ToString()
        {
            string summaryFormat = SafeResourceLoader.Get(
                "SyncResultSummary",
                "Serien: {0} verknüpft, {1} nicht gefunden · Episoden: {2} aktualisiert · Tracks: {3} angelegt");
            string summary = string.Format(
                CultureInfo.CurrentCulture, summaryFormat, SeriesMatched, SeriesUnmatched, EpisodesUpdated, TracksCreated);

            // Ordner da, aber kein einziger zugeordnet: Dann fehlen die Serien in der Datenbank,
            // und der Nutzer muss sie erst anlegen oder einlesen lassen.
            if (SeriesMatched == 0 && SeriesUnmatched > 0)
            {
                string hintFormat = SafeResourceLoader.Get(
                    "SyncResultNoMatchingSeries",
                    "{0} Ordner gefunden, aber keine passende Serie in der Datenbank vorhanden.");
                summary += "\n" + string.Format(CultureInfo.CurrentCulture, hintFormat, SeriesUnmatched);
            }

            return summary;
        }
    }
}
