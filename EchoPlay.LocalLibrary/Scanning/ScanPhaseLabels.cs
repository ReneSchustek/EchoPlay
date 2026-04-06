namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Konstanten für die Phasenbeschreibungen des vierphasigen Bibliotheks-Scans.
    /// Werden vom <see cref="ScanOrchestrator"/> in <see cref="ScanProgress.PhaseLabel"/> gesetzt.
    /// Die Texte sind bewusst auf Deutsch gehalten, da die App primär für den deutschen Markt ist.
    /// </summary>
    internal static class ScanPhaseLabels
    {
        /// <summary>Phase 1 – Audiodateien zählen, bevor der Scan startet.</summary>
        internal const string Preparation = "Vorbereitung …";

        /// <summary>Phase 2 – Serienordner erkennen und zuordnen.</summary>
        internal const string Series = "Serien werden ermittelt …";

        /// <summary>Phase 3 – Episodenordner pro Serie verarbeiten.</summary>
        internal const string Episodes = "Folgen werden ermittelt …";

        /// <summary>Phase 4 – ID3-Metadaten der Audiodateien lesen.</summary>
        internal const string Tracks = "Tracks werden gescannt …";
    }
}
