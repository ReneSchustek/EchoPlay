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
    /// Cover-Verwaltung: Datei-Picker und Online-Cover-Suche für Serien und Episoden.
    /// Offline-Modus wird vor jeder Online-Suche abgefragt.
    /// </summary>
    public sealed partial class MediathekLokalPage : Page
    {
        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog, damit der Nutzer manuell ein Bild als
        /// Serien-Cover auswählen kann. Die Bytes werden dann ans ViewModel übergeben.
        /// Der FileOpenPicker benötigt das HWND des Hauptfensters.
        /// </summary>
        private async void OnSeriesCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                byte[]? bytes = await PickImageFileAsync();

                if (bytes is not null)
                {
                    await ViewModel.ApplySeriesCoverFromBytesAsync(card, bytes);
                }
            });
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Serie.
        /// Im Dialog kann der Suchbegriff angepasst und die Suche wiederholt werden,
        /// bevor ein Ergebnis übernommen wird.
        /// </summary>
        private async void OnSeriesCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Offline-Modus: Nutzer fragen, bevor der Cover-Such-Dialog geöffnet wird
                using IDisposable? onlineScope = await ViewModel.RequestOnlineAccessForCoverSearchAsync();
                if (onlineScope is null) return;

                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, ct) => ViewModel.SearchCoversAsync(query, ct),
                    Content.XamlRoot);

                if (selected is not null)
                {
                    await ViewModel.ApplySelectedSeriesCoverAsync(card, selected);
                }
            });
        }

        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog für ein Episoden-Cover.
        /// </summary>
        private async void OnEpisodeCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            LocalEpisodeCardViewModel? card = ViewModel.Episodes.FirstOrDefault(ep => ep.EpisodeId == episodeId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                byte[]? bytes = await PickImageFileAsync();

                if (bytes is not null)
                {
                    await ViewModel.ApplyEpisodeCoverFromBytesAsync(card, bytes);
                }
            });
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Episode.
        /// </summary>
        private async void OnEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            LocalEpisodeCardViewModel? card = ViewModel.Episodes.FirstOrDefault(ep => ep.EpisodeId == episodeId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Offline-Modus: Nutzer fragen, bevor der Cover-Such-Dialog geöffnet wird
                using IDisposable? onlineScope = await ViewModel.RequestOnlineAccessForCoverSearchAsync();
                if (onlineScope is null) return;

                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, ct) => ViewModel.SearchCoversAsync(query, ct),
                    Content.XamlRoot);

                if (selected is not null)
                {
                    await ViewModel.ApplySelectedEpisodeCoverAsync(card, selected);
                }
            });
        }
    }
}
