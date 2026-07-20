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
        /// Lokale Serienordner ohne Datenbank-Treffer – kein Fehler, nur gezählt.
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
        /// erscheint ein Hinweis – das ist der häufigste Grund für "0 Ergebnisse" nach einem Erst-Scan.
        /// </summary>
        /// <returns>Formatierter Ergebnis-Text für die UI-Anzeige.</returns>

        public override string ToString()
        {
            string basis =
                $"Serien: {SeriesMatched} verknüpft, {SeriesUnmatched} nicht gefunden · " +
                $"Episoden: {EpisodesUpdated} aktualisiert · " +
                $"Tracks: {TracksCreated} angelegt";

            // Wenn Ordner gefunden wurden, aber kein einziger gematcht werden konnte,
            // sind noch keine Serien in der Datenbank vorhanden – erst importieren, dann scannen
            if (SeriesMatched == 0 && SeriesUnmatched > 0)
            {
                basis += $"\n{SeriesUnmatched} Ordner gefunden, aber keine passende Serie in der Datenbank vorhanden.";
            }

            return basis;
        }
    }
}
