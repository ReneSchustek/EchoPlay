using Microsoft.UI.Xaml;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Interface für Kachel-ViewModels, die im Akkordeon-Layout selektiert werden können.
    /// Steuert das V-Icon unter der Kachel und wird von beiden Mediathek-Pages
    /// über SeriesTileControl.SelectedIndicatorVisibility gebunden.
    /// </summary>
    public interface IAccordionSelectable
    {
        /// <summary>Gibt an, ob diese Kachel im Akkordeon aufgeklappt ist.</summary>
        bool IsSelectedInAccordion { get; set; }

        /// <summary>Sichtbarkeit des V-Pfeils unter der Kachel.</summary>
        Visibility SelectedIndicatorVisibility { get; }
    }
}
