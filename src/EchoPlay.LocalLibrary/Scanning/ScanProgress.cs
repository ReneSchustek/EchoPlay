namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Beschreibt den aktuellen Fortschritt eines laufenden Bibliotheks-Scans.
    /// Wird vom <see cref="LocalLibraryScanner"/>, vom <see cref="IScanOrchestrator"/>
    /// und vom SyncService an <c>IProgress&lt;ScanProgress&gt;</c>-Aufrufer gemeldet.
    /// </summary>
    public sealed class ScanProgress
    {
        /// <summary>
        /// Aktuelle Scan-Phase (1–4).
        /// <list type="bullet">
        ///   <item>1 – Vorbereitung (Dateianzahl ermitteln)</item>
        ///   <item>2 – Serien erkennen</item>
        ///   <item>3 – Folgen ermitteln</item>
        ///   <item>4 – Tracks scannen</item>
        /// </list>
        /// 0 bedeutet: keine Phasen-Information verfügbar (Legacy-Modus).
        /// </summary>
        public int Phase { get; init; }

        /// <summary>
        /// Lesbarer Name der aktuellen Phase, z.B. "Vorbereitung …".
        /// Leer wenn keine Phasen-Information verfügbar ist.
        /// </summary>
        public string PhaseLabel { get; init; } = string.Empty;

        /// <summary>
        /// Anzahl der bisher verarbeiteten Audiodateien.
        /// </summary>
        public int ProcessedFiles { get; init; }

        /// <summary>
        /// Gesamtanzahl der zu verarbeitenden Audiodateien.
        /// 0 bedeutet: Gesamtzahl unbekannt → Fortschrittsbalken indeterministisch.
        /// </summary>
        public int TotalFiles { get; init; }

        /// <summary>
        /// Anzahl der bisher mit der Datenbank abgeglichenen Serien.
        /// Wird vom SyncService während der DB-Sync-Phase gesetzt.
        /// </summary>
        public int ProcessedSeries { get; init; }

        /// <summary>
        /// Gesamtanzahl der zu synchronisierenden Serien.
        /// 0 bedeutet: unbekannt (Scan noch nicht abgeschlossen).
        /// </summary>
        public int TotalSeries { get; init; }

        /// <summary>
        /// Anzeigetext für die Status-Leiste, z.B. "Scanne TKKG …".
        /// </summary>
        public string StatusText { get; init; } = string.Empty;

        /// <summary>
        /// Optionaler Detail-Text, z.B. "12 von 76 Serien".
        /// Null wenn kein Detail verfügbar ist.
        /// </summary>
        public string? DetailText { get; init; }

        /// <summary>
        /// Fortschritt in Prozent (0–100).
        /// Priorität: Audiodateien (Scanner-Phase) vor Serien (DB-Sync-Phase).
        /// Gibt 0 zurück wenn beide Zähler unbekannt sind – indeterministischer Balken.
        /// </summary>
        public double PercentComplete =>
            TotalFiles > 0 ? ProcessedFiles / (double)TotalFiles * 100.0 :
            TotalSeries > 0 ? ProcessedSeries / (double)TotalSeries * 100.0 : 0;
    }
}
