namespace EchoPlay.App.Models
{
    /// <summary>
    /// Filtert die Serienübersicht nach dem Wiedergabefortschritt der Episoden.
    /// </summary>
    public enum SeriesStatusFilter
    {
        /// <summary>Alle Serien anzeigen, kein Fortschrittsfilter.</summary>
        All,

        /// <summary>Nur Serien, bei denen mindestens eine Episode noch nicht angehört wurde.</summary>
        New,

        /// <summary>Nur Serien, bei denen mindestens eine Episode angefangen aber noch nicht fertig gehört wurde.</summary>
        InProgress,

        /// <summary>Nur Serien, bei denen alle Episoden als vollständig gehört markiert sind.</summary>
        Finished
    }
}
