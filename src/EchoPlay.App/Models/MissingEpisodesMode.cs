namespace EchoPlay.App.Models
{
    /// <summary>
    /// Mögliche Ergebnisse des Drei-Optionen-Dialogs für die Fehlende-Folgen-Prüfung.
    /// Wird vom <see cref="EchoPlay.App.ViewModels.MediathekLokalViewModel"/> über
    /// das <c>MissingEpisodesModeRequested</c>-Event vom Code-Behind angefordert.
    /// </summary>
    public enum MissingEpisodesMode
    {
        /// <summary>Nutzer hat abgebrochen – keine Prüfung.</summary>
        Cancel,
        /// <summary>Nur lokale Ordnerstruktur prüfen (kein Netzwerk).</summary>
        OfflineOnly,
        /// <summary>Lokale Lücken + Live-Online-Abgleich per iTunes.</summary>
        WithOnline
    }
}
