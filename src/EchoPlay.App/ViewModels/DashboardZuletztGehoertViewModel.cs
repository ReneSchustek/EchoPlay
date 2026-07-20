namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „Zuletzt gehört"-Abschnitt des Dashboards.
    /// Hält die zuletzt gehörten Serien – pro Serie nur der jüngste Eintrag, sortiert nach
    /// Wiedergabezeitpunkt. Reiner Daten-Halter.
    /// </summary>
    public sealed class DashboardZuletztGehoertViewModel : DashboardListSectionViewModel<RecentSeriesCardViewModel>
    {
    }
}
