using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Kontextmenü-Handler der lokalen Mediathek: Details, Überwachen, Markieren, Entfernen,
    /// Löschen, Restrukturieren, Episoden-Status. Die Methoden greifen über <see cref="ViewModel"/>
    /// auf die gleiche VM-Instanz zu wie die Kern-Page.
    /// </summary>
    public sealed partial class LocalLibraryPage : Page
    {
        /// <summary>
        /// Navigiert zur Serien-Detailansicht.
        /// </summary>
        private void OnSeriesDetailsClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer lokalen Serie um.
        /// Beim Aktivieren wird sofort ein iTunes-Check für diese Serie ausgelöst,
        /// damit die Neuerscheinungen beim nächsten Dashboard-Besuch bereitstehen.
        /// </summary>
        private async void OnToggleWatchSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                bool isChecked = item.IsChecked;
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.ToggleWatchAsync(seriesId, isChecked));
            }
        }

        /// <summary>
        /// Markiert alle Folgen der Serie als gehört.
        /// </summary>
        private async void OnMarkAllAsReadClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.MarkAllAsReadAsync(seriesId));
            }
        }

        /// <summary>
        /// Löscht den Import einer Serie nach Bestätigung durch den Nutzer.
        /// Die SeriesId wird über das Tag-Property des MenuFlyoutItem übergeben.
        /// </summary>
        private async void OnRemoveSeriesFromLibraryClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.DeleteSeriesFromLibraryAsync(seriesId));
            }
        }

        /// <summary>
        /// Löscht die Serie aus der Bibliothek und ihren Ordner von der Platte, nach Bestätigung
        /// im Ansichtsmodell.
        /// </summary>
        private async void OnDeleteSeriesFromDiskClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                LocalArtistCardViewModel? card = ViewModel.ArtistsVM.Artists.FirstOrDefault(a => a.SeriesId == seriesId);
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.DeleteSeriesFromDiskAsync(seriesId, card?.LocalFolderPath));
            }
        }

        /// <summary>
        /// Markiert eine Episode als gehört.
        /// </summary>
        private async void OnEpisodeMarkPlayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.EpisodesVM.MarkEpisodeAsPlayedAsync(episodeId));
        }

        /// <summary>
        /// Markiert eine Episode als ungehört.
        /// </summary>
        private async void OnEpisodeMarkUnplayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.EpisodesVM.MarkEpisodeAsUnplayedAsync(episodeId));
        }

        /// <summary>
        /// Startet den Ordnerstruktur-Assistenten für die gewählte Serie.
        /// Zeigt eine Vorschau der geplanten Verschiebungen und führt sie bei Bestätigung aus.
        /// </summary>
        private async void OnRestructureClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => RestructureAsync(seriesId));
        }

        /// <summary>Erstellt die Vorschau, fragt nach und baut bei Bestätigung um.</summary>
        private async Task RestructureAsync(Guid seriesId)
        {
            RestructurePreviewDisplay? preview = await AnalyzeRestructureAsync(seriesId);
            if (preview is null || preview.IsEmpty)
            {
                return;
            }

            using Helpers.DialogReentrancyGuard? restructureGuard = Helpers.DialogReentrancyGuard.TryAcquire();
            if (restructureGuard is null) return;

            ContentDialogResult result = await Helpers.ScrollableTextDialog.ShowAsync(
                Content.XamlRoot,
                title: _resources.GetString("RestructureDialogTitle"),
                content: RestructurePreviewText(preview),
                new Helpers.ScrollableTextDialogOptions
                {
                    PrimaryButtonText = _resources.GetString("RestructureExecuteButton"),
                    CloseButtonText = _resources.GetString("CommonCancelButton"),
                    UseMonospace = true,
                    MonospaceFontSize = 12,
                    DefaultButton = ContentDialogButton.Close,
                });

            // Nach dem Umbau: Bibliothek neu laden, damit die neue Struktur sichtbar wird
            if (result == ContentDialogResult.Primary
                && await ViewModel.Actions.ExecuteRestructureAsync(seriesId, preview) > 0)
            {
                await ViewModel.Actions.LoadAsync();
            }
        }

        /// <summary>
        /// Erstellt die Vorschau im Hintergrund. Lokale Methode statt Lambda, denn <c>+=</c> und
        /// <c>-=</c> müssen dieselbe Delegate-Identität haben, sonst ist das Abmelden wirkungslos
        /// und der Handler hängt am ViewModel weiter.
        /// </summary>
        private async Task<RestructurePreviewDisplay?> AnalyzeRestructureAsync(Guid seriesId)
        {
            RestructurePreviewDisplay? preview = null;
            void CapturePreview(RestructurePreviewDisplay p) => preview = p;

            ViewModel.Actions.RestructurePreviewReady += CapturePreview;
            try
            {
                await ViewModel.Actions.AnalyzeRestructureAsync(seriesId);
            }
            finally
            {
                ViewModel.Actions.RestructurePreviewReady -= CapturePreview;
            }

            return preview;
        }

        /// <summary>Der Vorschau-Text: Zusammenfassung, dann je Datei ihr Zielordner.</summary>
        private string RestructurePreviewText(RestructurePreviewDisplay preview)
        {
            System.Text.StringBuilder sb = new();
            _ = sb.AppendLine(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _resources.GetString("RestructurePreviewSummary"),
                preview.FileCount,
                preview.FolderCount));
            _ = sb.AppendLine();

            foreach (RestructureActionDisplay action in preview.Actions)
            {
                _ = sb.Append("  ").AppendLine(action.FileName);
                _ = sb.Append("    \u2192 ").Append(action.TargetFolderName).AppendLine("/");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Benennt die Folgenordner der gewählten Serie nach dem eingestellten Muster um, mit
        /// dem Titel aus dem Album-Tag. Zeigt erst die Vorschau, danach das Ergebnis.
        /// </summary>
        private async void OnFolderRenameClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                EchoPlay.LocalLibrary.Models.FolderRenamePlan? plan = await ViewModel.FolderRename.PlanAsync(seriesId);
                if (plan is null)
                {
                    return;
                }

                using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
                if (guard is null) return;

                if (await ShowFolderRenamePreviewAsync(plan) != ContentDialogResult.Primary)
                {
                    return;
                }

                EchoPlay.LocalLibrary.Models.FolderRenameResult result = await ViewModel.FolderRename.ExecuteAsync(seriesId, plan);
                _ = await Helpers.ScrollableTextDialog.ShowAsync(
                    Content.XamlRoot,
                    title: _resources.GetString("FolderRenameDialogTitle"),
                    content: ViewModels.FolderRenamePreviewText.BuildResult(result),
                    new Helpers.ScrollableTextDialogOptions
                    {
                        CloseButtonText = _resources.GetString("CommonCloseButton"),
                    });
            });
        }

        /// <summary>
        /// Zeigt die geplanten Umbenennungen; ohne eine einzige gibt es nur „Schließen“.
        /// </summary>
        private Task<ContentDialogResult> ShowFolderRenamePreviewAsync(EchoPlay.LocalLibrary.Models.FolderRenamePlan plan)
        {
            bool canRename = plan.Renames.Count > 0;
            return Helpers.ScrollableTextDialog.ShowAsync(
                Content.XamlRoot,
                title: _resources.GetString("FolderRenameDialogTitle"),
                content: ViewModels.FolderRenamePreviewText.Build(plan),
                new Helpers.ScrollableTextDialogOptions
                {
                    PrimaryButtonText = canRename ? _resources.GetString("FolderRenameExecuteButton") : null,
                    CloseButtonText = _resources.GetString(canRename ? "CommonCancelButton" : "CommonCloseButton"),
                    MaxHeight = 500,
                    UseMonospace = true,
                    MonospaceFontSize = 12,
                    DefaultButton = ContentDialogButton.Close,
                });
        }
    }
}
