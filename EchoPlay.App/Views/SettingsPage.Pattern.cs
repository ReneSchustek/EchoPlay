using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Muster-Analyse für Episoden-Pfade: Startet die Analyse und zeigt den
    /// Auswahl-Dialog mit RadioButtons für die Vorschläge.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>
        /// Startet die Mustererkennung für den konfigurierten Bibliotheksordner.
        /// </summary>
        private async void OnAnalyzePatternClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.AnalyzePatternAsync());
        }

        /// <summary>
        /// Übernimmt das angeklickte Muster in das Episodenmuster-Textfeld.
        /// Das Tag des Buttons enthält das Muster als String.
        /// </summary>
        private void OnPatternSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pattern })
            {
                ViewModel.ApplyPatternSuggestion(pattern);
            }
        }

        /// <summary>
        /// Event-Handler für <see cref="ViewModels.SettingsViewModel.PatternSelectionRequested"/>.
        /// Delegiert an den asynchronen Dialog-Methode.
        /// async void ist hier korrekt – WinUI-Event-Handler dürfen keinen Task zurückgeben.
        /// </summary>
        private async void OnPatternSelectionRequested(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ShowPatternSelectionDialogAsync(suggestions));
        }

        /// <summary>
        /// Zeigt einen ContentDialog mit RadioButtons für jeden Muster-Vorschlag.
        /// Der Nutzer wählt eines der Muster aus und bestätigt – erst dann wird es übernommen.
        /// </summary>
        private async Task ShowPatternSelectionDialogAsync(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            StackPanel contentPanel = new() { Spacing = 8 };

            RadioButton? firstButton = null;

            foreach (PatternSuggestionDisplay suggestion in suggestions)
            {
                // Beschreibungstext: Trefferquote in Prozent oder Hinweis auf flache Struktur
                string description = suggestion.IsFlatStructure
                    ? _localizationService.Get("PatternDialogFlatHint")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        _localizationService.Get("PatternDialogMatchFormat"),
                        (int)(suggestion.MatchPercentage * 100));

                StackPanel itemPanel = new() { Spacing = 2 };

                // Muster in Monospace-Schrift – erleichtert das Lesen der Platzhalter
                RadioButton radio = new()
                {
                    Content = suggestion.Pattern,
                    Tag = suggestion.Pattern,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    IsChecked = firstButton is null
                };

                TextBlock descBlock = new()
                {
                    Text = description,
                    FontSize = 12,
                    Margin = new Thickness(28, 0, 0, 0),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };

                itemPanel.Children.Add(radio);
                itemPanel.Children.Add(descBlock);
                contentPanel.Children.Add(itemPanel);

                firstButton ??= radio;
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = _localizationService.Get("PatternDialogTitle"),
                Content = contentPanel,
                PrimaryButtonText = _localizationService.Get("PatternDialogApply"),
                CloseButtonText = _localizationService.Get("PatternDialogCancel")
            };

            Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            // Ausgewählten RadioButton finden und Muster übernehmen
            foreach (UIElement element in contentPanel.Children)
            {
                if (element is not StackPanel panel)
                {
                    continue;
                }

                foreach (UIElement child in panel.Children)
                {
                    if (child is RadioButton { IsChecked: true, Tag: string pattern })
                    {
                        ViewModel.ApplyPatternSuggestion(pattern);
                        return;
                    }
                }
            }
        }
    }
}
