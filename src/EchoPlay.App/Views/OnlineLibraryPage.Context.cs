using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Kontextmenü der Online-Mediathek: Serien-Details, Überwachung, Entfernen und die
    /// Cover-Suche für eine Folge.
    /// </summary>
    public sealed partial class OnlineLibraryPage : Page
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
        /// Schaltet den Überwachungsstatus einer Serie um. Die Logik (Cache + iTunes-Check)
        /// liegt im <see cref="OnlineLibraryViewModel"/> bzw. dem zugehörigen Service.
        /// </summary>
        private async void OnToggleWatchSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                bool isChecked = item.IsChecked;
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ToggleWatchAsync(seriesId, isChecked));
            }
        }

        /// <summary>
        /// Entfernt eine Serie aus der Online-Mediathek, nach Bestätigung im Ansichtsmodell.
        /// </summary>
        private async void OnRemoveOnlineSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.RemoveSeriesAsync(seriesId));
            }
        }

        /// <summary>
        /// Öffnet den Cover-Such-Dialog für eine Online-Episode.
        /// Nutzt den gleichen <see cref="Helpers.CoverSearchDialog"/> wie die lokale Mediathek.
        /// Such- und Apply-Logik liegt im ViewModel; die Page weiß weder etwas vom DI-Scope
        /// noch vom LocalLibrary-Modell.
        /// </summary>
        private async void OnOnlineEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId }) return;

            OnlineEpisodeCardViewModel? card = null;
            foreach (OnlineEpisodeCardViewModel ep in ViewModel.Episodes)
            {
                if (ep.EpisodeId == episodeId) { card = ep; break; }
            }

            if (card is null) return;

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, page, ct) => ViewModel.SearchEpisodeCoversAsync(query, page, ct),
                    Content.XamlRoot);

                if (selected is null) return;

                await ViewModel.ApplySelectedEpisodeCoverAsync(card, selected);
            });
        }
    }
}
