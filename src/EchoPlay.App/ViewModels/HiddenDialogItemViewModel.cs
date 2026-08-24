using EchoPlay.Core.Models;
using Microsoft.UI.Xaml;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Eine Zeile der Liste ausgeblendeter Hinweise in den Einstellungen.
    /// </summary>
    /// <remarks>
    /// Jede Zeile bringt ihre eigene Schaltfläche mit. Der Umweg über einen Befehl mit
    /// Parameter wäre in einer Vorlage nur mit <c>DataContext</c>-Griffen zu binden — so
    /// steht in der Vorlage schlicht <c>{x:Bind RestoreCommand}</c>.
    /// </remarks>
    /// <param name="Key">Der ausgeblendete Dialog.</param>
    /// <param name="DisplayName">Sprechender Name für die Liste.</param>
    /// <param name="SuppressedAtText">Zeitpunkt des Ausblendens in Ortszeit.</param>
    /// <param name="IsIrreversible">
    /// Ob die zugehörige Aktion Dateien ohne Rückweg schreibt. Solche Zeilen bekommen einen
    /// zusätzlichen Warnhinweis.
    /// </param>
    /// <param name="RestoreCommand">Holt genau diesen Hinweis zurück in die Anzeige.</param>
    public sealed record HiddenDialogItemViewModel(
        DialogKey Key,
        string DisplayName,
        string SuppressedAtText,
        bool IsIrreversible,
        ICommand RestoreCommand)
    {
        /// <summary>Sichtbarkeit des Warnhinweises für Aktionen ohne Rückweg.</summary>
        public Visibility IrreversibleVisibility =>
            IsIrreversible ? Visibility.Visible : Visibility.Collapsed;
    }
}
