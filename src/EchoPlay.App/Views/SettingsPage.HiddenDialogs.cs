using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Teil der Einstellungsseite für den Abschnitt „Ausgeblendete Hinweise".
    /// Die Schaltfläche je Zeile hängt am Befehl des Zeilen-Ansichtsmodells; hier liegt
    /// nur das gesammelte Zurückholen, das keine Zeile für sich beanspruchen kann.
    /// </summary>
    public sealed partial class SettingsPage
    {
        /// <summary>
        /// Holt alle ausgeblendeten Hinweise zurück in die Anzeige.
        /// </summary>
        /// <param name="sender">Die auslösende Schaltfläche.</param>
        /// <param name="e">Ereignisargumente des Klicks.</param>
        private async void OnRestoreAllHiddenDialogsClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(ViewModel.HiddenDialogsVM.RestoreAllAsync);
        }
    }
}
