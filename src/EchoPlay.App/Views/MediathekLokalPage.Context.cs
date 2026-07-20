using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Kontextmenü-Handler der lokalen Mediathek: Details, Überwachen, Markieren, Entfernen,
    /// Löschen, Restrukturieren, Episoden-Status. Die Methoden greifen über <see cref="ViewModel"/>
    /// auf die gleiche VM-Instanz zu wie die Kern-Page.
    /// </summary>
    public sealed partial class MediathekLokalPage : Page
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
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ToggleWatchAsync(seriesId, isChecked));
            }
        }

        private async void OnMarkAllAsReadClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkAllAsReadAsync(seriesId));
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
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.DeleteSeriesFromLibraryAsync(seriesId));
            }
        }

        private async void OnDeleteSeriesFromDiskClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.DeleteSeriesFromDiskAsync(seriesId, card?.LocalFolderPath));
            }
        }

        /// <summary>
        /// Markiert eine Episode als gehört (nach Bestätigung).
        /// </summary>
        private async void OnEpisodeMarkPlayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkEpisodeAsPlayedAsync(episodeId));
        }

        /// <summary>
        /// Markiert eine Episode als ungehört (nach Bestätigung).
        /// </summary>
        private async void OnEpisodeMarkUnplayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkEpisodeAsUnplayedAsync(episodeId));
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

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Vorschau im Hintergrund erstellen.
                // Lokale Methode statt Lambda — `+=` und `-=` müssen dieselbe Delegate-Identität haben,
                // sonst ist das Unsubscribe ein No-Op und der Handler hängt am VM weiter.
                RestructurePreviewDisplay? preview = null;
                void CapturePreview(RestructurePreviewDisplay p) => preview = p;

                ViewModel.RestructurePreviewReady += CapturePreview;
                try
                {
                    await ViewModel.AnalyzeRestructureAsync(seriesId);
                }
                finally
                {
                    ViewModel.RestructurePreviewReady -= CapturePreview;
                }

                if (preview is null || preview.IsEmpty)
                {
                    return;
                }

                using Helpers.DialogReentrancyGuard? restructureGuard = Helpers.DialogReentrancyGuard.TryAcquire();
                if (restructureGuard is null) return;

                // Vorschau-Text zusammenbauen
                System.Text.StringBuilder sb = new();
                _ = sb.Append(preview.FileCount).Append(" Dateien \u2192 ").Append(preview.FolderCount).AppendLine(" Ordner");
                _ = sb.AppendLine();

                foreach (RestructureActionDisplay action in preview.Actions)
                {
                    _ = sb.Append("  ").AppendLine(action.FileName);
                    _ = sb.Append("    \u2192 ").Append(action.TargetFolderName).AppendLine("/");
                }

                ContentDialogResult result = await Helpers.ScrollableTextDialog.ShowAsync(
                    Content.XamlRoot,
                    title: _resources.GetString("RestructureDialogTitle"),
                    content: sb.ToString(),
                    primaryButtonText: _resources.GetString("RestructureExecuteButton"),
                    closeButtonText: _resources.GetString("CommonCancelButton"),
                    useMonospace: true,
                    monospaceFontSize: 12,
                    defaultButton: ContentDialogButton.Close);

                if (result == ContentDialogResult.Primary)
                {
                    int movedCount = await ViewModel.ExecuteRestructureAsync(preview);

                    // Nach dem Umbau: Bibliothek neu laden, damit die neue Struktur sichtbar wird
                    if (movedCount > 0)
                    {
                        await ViewModel.LoadAsync();
                    }
                }
            });
        }
    }
}
