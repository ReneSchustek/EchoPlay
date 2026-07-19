namespace EchoPlay.App.Services
{
    /// <summary>
    /// Priorität einer Cover-Lade-Anfrage im Zusammenspiel aus Hintergrund-Scan
    /// und Foreground-Aktionen. Foreground bedeutet: die UI zeigt gerade die
    /// betroffenen Folgen an; der Hintergrund-Loop pausiert, bis die sichtbare
    /// Serie versorgt ist. Background ist die Standard-Stufe für den periodischen
    /// Cover-Scan und das vom Dashboard angestoßene Nachladen von Kachel-Covern.
    /// </summary>

    public enum CoverFetchPriority
    {
        /// <summary>Periodischer Hintergrund-Scan – wird von Foreground-Anfragen überholt.</summary>
        Background,

        /// <summary>Sichtbare UI-Anfrage – erhält Vorrang vor Background-Consumern.</summary>
        Foreground
    }
}
