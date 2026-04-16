using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Datenbankpflege und Reset-Dialoge: NumberBox-Handler für Aufbewahrungstage
    /// und Neuerscheinungs-Fenster sowie der Bestätigungsdialog vor dem Reset.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>
        /// Startet die Datenbankbereinigung mit den aktuell konfigurierten Einstellungen.
        /// </summary>
        private async void OnDbMaintenanceClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                await ViewModel.RunMaintenanceAsync();

                // Abschlussdialog – der Nutzer sieht klar, ob die Bereinigung geklappt hat.
                // MaintenanceStatusText enthält bei Erfolg eine Bestätigung, bei Fehler die Meldung.
                ContentDialog resultDialog = new()
                {
                    XamlRoot        = XamlRoot,
                    Title           = _resources.GetString("DatabaseMaintenanceTitle"),
                    Content         = new TextBlock
                    {
                        Text         = ViewModel.MaintenanceStatusText,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    },
                    CloseButtonText = _resources.GetString("CommonCloseButton")
                };

                Helpers.ContentDialogDragHelper.MakeDraggable(resultDialog);
                _ = await resultDialog.ShowAsync();
            });
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der NumberBox in die ViewModel-Property.
        /// NumberBox.Value ist <see langword="double"/> – explizite Konvertierung in <see langword="int"/> nötig.
        /// </summary>
        private void OnDbPurgeDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // NaN tritt auf, wenn der Nutzer ein ungültiges Zeichen eingibt – ignorieren
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.DbPurgeDays = (int)args.NewValue;
            }
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der Neuerscheinungen-NumberBox in die ViewModel-Property.
        /// </summary>
        private void OnNewReleaseDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.NewReleaseDays = (int)args.NewValue;
            }
        }

        /// <summary>
        /// Setzt die Bibliothek zurück – je nach gewähltem Scope: Online, Lokal oder Alle.
        /// </summary>
        private async void OnResetClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Scope aus RadioButtons ermitteln
                int selectedIndex = ResetScopeRadio.SelectedIndex;
                string scopeLabel = selectedIndex switch
                {
                    0 => "Online",
                    1 => "Lokal",
                    _ => "Alle"
                };

                TextBlock contentText = new()
                {
                    Text        = _localizationService.Get("DbResetDialogDescription"),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                };

                ContentDialog dialog = new()
                {
                    XamlRoot          = XamlRoot,
                    Title             = $"{_localizationService.Get("DbResetDialogTitle")} ({scopeLabel})",
                    Content           = contentText,
                    PrimaryButtonText = _localizationService.Get("DbResetDialogConfirm"),
                    CloseButtonText   = _localizationService.Get("PatternDialogCancel")
                };

                Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
                ContentDialogResult result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    await ViewModel.ResetLibraryAsync(selectedIndex);
                }
            });
        }
    }
}
