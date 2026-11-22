using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App
{
    /// <summary>
    /// Die Info-Leiste im Hauptfenster: Wechsel von Theme und Sprache samt Lade-Overlay.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>
        /// Blendet den Theme-Lade-Overlay ein.
        /// Wird aufgerufen, bevor das neue Theme angewendet wird, damit der Nutzer visuelles Feedback erhält.
        /// </summary>
        internal void ShowThemeOverlay() => ThemeLoadingOverlay.Visibility = Visibility.Visible;

        /// <summary>
        /// Blendet den Theme-Lade-Overlay aus.
        /// Wird aufgerufen, nachdem das neue Theme vollständig angewendet wurde.
        /// </summary>
        internal void HideThemeOverlay() => ThemeLoadingOverlay.Visibility = Visibility.Collapsed;

        /// <summary>
        /// Wechselt das Theme über das MenuFlyout der Info-Leiste.
        /// Das Tag des MenuFlyoutItem enthält den Theme-Namen.
        /// Zeigt kurz einen Lade-Overlay, damit der Nutzer Feedback über den laufenden Wechsel erhält.
        /// </summary>
        /// <param name="sender">Das ausgelöste MenuFlyoutItem.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnStatusBarThemeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string themeName })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                ShowThemeOverlay();

                // Ein Frame dauert gut 16 ms; nach 50 ms ist die Einblendung sicher gezeichnet,
                // bevor das Umschalten den UI-Thread belegt.
                await Task.Delay(50);

                StatusBar.SwitchTheme(themeName);

                // Die neuen Farben brauchen einen Layout-Durchlauf; ohne die Pause verschwände die
                // Einblendung, bevor sie zu sehen ist.
                await Task.Delay(150);

                HideThemeOverlay();
            });
        }

        /// <summary>
        /// Wechselt die Sprache über das MenuFlyout der Info-Leiste und startet die App neu.
        /// Das Tag des MenuFlyoutItem enthält den BCP-47-Sprachcode.
        /// </summary>
        /// <param name="sender">Das ausgelöste MenuFlyoutItem.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnStatusBarLanguageClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string languageCode)
            {
                await AsyncEventHandler.RunSafelyAsync(() => StatusBar.ChangeLanguageAsync(languageCode));
            }
        }
    }
}
