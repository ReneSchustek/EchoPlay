using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

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
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.LocalVM.AnalyzePatternAsync());
        }

        /// <summary>
        /// Übernimmt das angeklickte Muster in das Episodenmuster-Textfeld.
        /// Das Tag des Buttons enthält das Muster als String.
        /// </summary>
        private void OnPatternSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pattern })
            {
                ViewModel.LocalVM.ApplyPatternSuggestion(pattern);
            }
        }

        /// <summary>
        /// Event-Handler für <see cref="ViewModels.LocalSettingsViewModel.PatternSelectionRequested"/>.
        /// Delegiert an den asynchronen Dialog-Methode.
        /// async void ist hier korrekt, denn WinUI-Event-Handler dürfen keinen Task zurückgeben.
        /// </summary>
        private async void OnPatternSelectionRequested(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ShowPatternSelectionDialogAsync(suggestions));
        }

        /// <summary>
        /// Zeigt einen ContentDialog mit RadioButtons für jeden Muster-Vorschlag.
        /// Der Nutzer wählt eines der Muster aus und bestätigt. Erst dann wird es übernommen.
        /// </summary>
        private async Task ShowPatternSelectionDialogAsync(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            StackPanel contentPanel = new() { Spacing = 8 };
            List<RadioButton> choices = [];
            foreach (PatternSuggestionDisplay suggestion in suggestions)
            {
                RadioButton radio = PatternChoice(suggestion, isFirst: choices.Count == 0);
                choices.Add(radio);
                contentPanel.Children.Add(new StackPanel
                {
                    Spacing = 2,
                    Children = { radio, PatternDescription(suggestion) },
                });
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
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && choices.FirstOrDefault(c => c.IsChecked == true) is { Tag: string pattern })
            {
                ViewModel.LocalVM.ApplyPatternSuggestion(pattern);
            }
        }

        /// <summary>
        /// Die Auswahl für ein Muster, in Monospace-Schrift; das erleichtert das Lesen der Platzhalter.
        /// Die Gruppe ist ausdrücklich gesetzt: Jede Auswahl steht in einem eigenen Panel, und ohne
        /// Gruppennamen schlössen sich die Felder nicht gegenseitig aus.
        /// </summary>
        private static RadioButton PatternChoice(PatternSuggestionDisplay suggestion, bool isFirst) => new()
        {
            GroupName = "PatternChoice",
            Content = suggestion.Pattern,
            Tag = suggestion.Pattern,
            FontFamily = Helpers.ThemeFonts.Mono(),
            IsChecked = isFirst
        };

        /// <summary>Beschreibungstext: Trefferquote in Prozent oder Hinweis auf flache Struktur.</summary>
        private TextBlock PatternDescription(PatternSuggestionDisplay suggestion) => new()
        {
            Text = suggestion.IsFlatStructure
                ? _localizationService.Get("PatternDialogFlatHint")
                : string.Format(
                    CultureInfo.CurrentCulture,
                    _localizationService.Get("PatternDialogMatchFormat"),
                    (int)(suggestion.MatchPercentage * 100)),
            FontSize = 12,
            Margin = new Thickness(28, 0, 0, 0),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
    }
}
