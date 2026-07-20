namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „Weiterhören"-Abschnitt des Dashboards.
    /// Hält angefangene Serien mit noch ungehörten Folgen – der Nutzer hat mindestens eine
    /// Folge gehört, aber die Serie ist noch nicht vollständig abgeschlossen.
    /// Reiner Daten-Halter: das Top-VM befüllt über <see cref="DashboardListSectionViewModel{T}.SetItems"/>.
    /// </summary>
    public sealed class DashboardWeiterhoerenViewModel : DashboardListSectionViewModel<UnheardSeriesCardViewModel>
    {
    }
}
