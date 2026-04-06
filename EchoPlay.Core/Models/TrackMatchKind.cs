namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Beschreibt das Ergebnis des Abgleichs zwischen lokalen Audiodateien und Online-Trackdaten.
    /// </summary>
    public enum TrackMatchKind
    {
        /// <summary>
        /// Noch kein Abgleich durchgeführt.
        /// </summary>
        NotMatched = 0,

        /// <summary>
        /// Track-by-Track – lokale und Online-Tracks stimmen in Anzahl und Struktur überein.
        /// Gilt wenn lokale Trackanzahl gleich Online-Trackanzahl und beide ≤ 20.
        /// </summary>
        TbT = 1,

        /// <summary>
        /// Streaming-Struktur – viele kurze Tracks auf beiden Seiten.
        /// Gilt wenn lokale und Online-Trackanzahl beide > 20.
        /// </summary>
        Streaming = 2,

        /// <summary>
        /// Manuell konfiguriert oder keiner der automatischen Regeln trifft zu.
        /// </summary>
        Custom = 3
    }
}
