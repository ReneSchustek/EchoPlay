namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Beschreibt den Wiedergabefortschritt einer Episode aus Sicht der UI.
    /// Wird aus dem persistierten <see cref="EchoPlay.Data.Entities.Playback.PlaybackState"/> abgeleitet.
    /// </summary>
    public enum PlaybackStatus
    {
        /// <summary>Noch nicht angehört.</summary>
        NotStarted,

        /// <summary>Teilweise angehört, aber noch nicht abgeschlossen.</summary>
        InProgress,

        /// <summary>Vollständig gehört.</summary>
        Finished
    }
}
