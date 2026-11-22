using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// „Daten bearbeiten" für Serie und Folge der lokalen Mediathek. Der Dialog prüft die
    /// Eingaben, bevor er schließt; ein Fehler bleibt im Dialog stehen, damit nichts von dem
    /// Eingegebenen verloren geht.
    /// </summary>
    public sealed partial class LocalLibraryPage : Page
    {
        /// <summary>
        /// Bearbeitet Titel, Nummer, Interpret und Jahr einer Folge.
        /// </summary>
        private async void OnEpisodeEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId } || ViewModel.MetadataEditor is not ILocalMetadataEditor editor)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => EditEpisodeAsync(editor, episodeId));
        }

        /// <summary>Lädt die Folge, fragt die Änderungen ab, speichert und zeigt die neue Liste.</summary>
        private async Task EditEpisodeAsync(ILocalMetadataEditor editor, Guid episodeId)
        {
            EpisodeEditData? current = await editor.LoadEpisodeAsync(episodeId);
            if (current is null)
            {
                return;
            }

            using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
            if (guard is null) return;

            EpisodeEditData? edited = await AskEpisodeEditsAsync(current);
            if (edited is null)
            {
                return;
            }

            EpisodeEditOutcome outcome = await editor.SaveEpisodeAsync(edited);

            // Nur die Folgen der gewählten Serie neu laden: Titel und Nummer der Kacheln
            // sind einmalig gebunden und ändern sich sonst nicht.
            if (ViewModel.ArtistsVM.SelectedArtist is LocalArtistCardViewModel artist)
            {
                await ViewModel.Actions.SelectArtistAsync(artist);
            }

            if (outcome.TagsFailed > 0)
            {
                _ = await Helpers.ScrollableTextDialog.ShowAsync(
                    Content.XamlRoot,
                    title: _resources.GetString("EditEpisodeDialogTitle"),
                    content: string.Format(CultureInfo.CurrentCulture, _resources.GetString("EditTagsFailedFormat"), outcome.TagsFailed),
                    new Helpers.ScrollableTextDialogOptions
                    {
                        CloseButtonText = _resources.GetString("CommonCloseButton"),
                    });
            }
        }

        /// <summary>
        /// Zeigt den Dialog mit Titel, Nummer, Interpret und Jahr. Er prüft vor dem Schließen.
        /// </summary>
        /// <returns>Die geänderten Daten, oder leer, wenn abgebrochen wurde.</returns>
        private async Task<EpisodeEditData?> AskEpisodeEditsAsync(EpisodeEditData current)
        {
            TextBox titleBox = new() { Header = _resources.GetString("EditFieldTitle"), Text = current.Title };
            TextBox numberBox = NumberBox("EditFieldNumber", current.Number);
            TextBox artistBox = new() { Header = _resources.GetString("EditFieldArtist"), Text = current.Artist ?? string.Empty };
            TextBox yearBox = NumberBox("EditFieldYear", current.Year);
            TextBlock hint = new() { Text = _resources.GetString("EditEpisodeHint"), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
            TextBlock error = ErrorText();

            ContentDialog dialog = EditDialog("EditEpisodeDialogTitle", titleBox, numberBox, artistBox, yearBox, hint, error);
            int? number = null;
            int? year = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                string? problem = LocalMetadataValidation.ValidateEpisode(
                    titleBox.Text, numberBox.Text, yearBox.Text, current.Number is null ? EpisodeNumberRule.Optional : EpisodeNumberRule.Required, out number, out year);
                ShowProblem(error, problem, args);
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? current with { Title = titleBox.Text.Trim(), Number = number, Artist = artistBox.Text, Year = year }
                : null;
        }

        /// <summary>Ein Eingabefeld für eine Zahl, vorbelegt mit dem bisherigen Wert.</summary>
        private TextBox NumberBox(string headerKey, int? value) => new()
        {
            Header = _resources.GetString(headerKey),
            Text = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            InputScope = NumberInputScope()
        };

        /// <summary>
        /// Bearbeitet den Titel einer Serie.
        /// </summary>
        private async void OnSeriesEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId } || ViewModel.MetadataEditor is not ILocalMetadataEditor editor)
            {
                return;
            }

            LocalArtistCardViewModel? card = ViewModel.ArtistsVM.AllArtists.FirstOrDefault(a => a.SeriesId == seriesId);
            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
                if (guard is null) return;

                TextBox titleBox = new() { Header = _resources.GetString("EditFieldTitle"), Text = card.Title };
                TextBlock error = ErrorText();
                ContentDialog dialog = EditDialog("EditSeriesDialogTitle", titleBox, error);
                dialog.PrimaryButtonClick += (_, args) => ShowProblem(error, LocalMetadataValidation.ValidateSeries(titleBox.Text), args);

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                if (await editor.SaveSeriesTitleAsync(seriesId, titleBox.Text))
                {
                    await ViewModel.Actions.LoadAsync();
                }
            });
        }

        private ContentDialog EditDialog(string titleKey, params UIElement[] fields)
        {
            StackPanel panel = new() { Spacing = 12, MinWidth = 420 };
            foreach (UIElement field in fields)
            {
                panel.Children.Add(field);
            }

            return new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = _resources.GetString(titleKey),
                Content = panel,
                PrimaryButtonText = _resources.GetString("EditSaveButton"),
                CloseButtonText = _resources.GetString("CommonCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };
        }

        private static TextBlock ErrorText() => new()
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        };

        private void ShowProblem(TextBlock error, string? problemKey, ContentDialogButtonClickEventArgs args)
        {
            if (problemKey is null)
            {
                return;
            }

            args.Cancel = true;
            error.Text = _resources.GetString(problemKey);
            error.Visibility = Visibility.Visible;
        }

        private static Microsoft.UI.Xaml.Input.InputScope NumberInputScope()
        {
            Microsoft.UI.Xaml.Input.InputScope scope = new();
            scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number));
            return scope;
        }
    }
}
