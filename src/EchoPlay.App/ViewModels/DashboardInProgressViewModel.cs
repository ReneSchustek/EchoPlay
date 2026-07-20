namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „In Progress"-Abschnitt des Dashboards.
    /// Hält die aktuell laufenden Episoden – Wiedergabestand größer als Null, noch nicht
    /// abgeschlossen – sortiert nach letzter Wiedergabe. Reiner Daten-Halter.
    /// </summary>
    public sealed class DashboardInProgressViewModel : DashboardListSectionViewModel<NewEpisodeCardViewModel>
    {
    }
}
