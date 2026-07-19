namespace EchoPlay.App.Models
{
    /// <summary>
    /// Filtert die Serienübersicht nach dem Wiedergabefortschritt der Episoden.
    /// </summary>
    public enum SeriesStatusFilter
    {
        /// <summary>Alle Serien anzeigen – kein Fortschrittsfilter.</summary>
        Alle,

        /// <summary>Nur Serien, bei denen mindestens eine Episode noch nicht angehört wurde.</summary>
        Neu,

        /// <summary>Nur Serien, bei denen mindestens eine Episode angefangen aber noch nicht fertig gehört wurde.</summary>
        AmHoeren,

        /// <summary>Nur Serien, bei denen alle Episoden als vollständig gehört markiert sind.</summary>
        Gehört
    }
}
