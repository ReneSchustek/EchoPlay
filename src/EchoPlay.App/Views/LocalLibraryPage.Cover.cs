using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Cover-Verwaltung: Datei-Picker und Online-Cover-Suche für Serien und Episoden.
    /// Offline-Modus wird vor jeder Online-Suche abgefragt.
    /// </summary>
    public sealed partial class LocalLibraryPage : Page
    {
        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog, damit der Nutzer manuell ein Bild als
        /// Serien-Cover auswählen kann. Die Bytes werden dann ans ViewModel übergeben.
        /// Der FileOpenPicker benötigt das HWND des Hauptfensters.
        /// </summary>
        private async void OnSeriesCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (FindSeriesCard(sender) is LocalArtistCardViewModel card)
            {
                await PickCoverAndApplyAsync(bytes => ViewModel.Actions.ApplySeriesCoverFromBytesAsync(card, bytes));
            }
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Serie.
        /// Im Dialog kann der Suchbegriff angepasst und die Suche wiederholt werden,
        /// bevor ein Ergebnis übernommen wird.
        /// </summary>
        private async void OnSeriesCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (FindSeriesCard(sender) is LocalArtistCardViewModel card)
            {
                await SearchCoverAndApplyAsync(card.Title, hit => ViewModel.Actions.ApplySelectedSeriesCoverAsync(card, hit));
            }
        }

        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog für ein Episoden-Cover.
        /// </summary>
        private async void OnEpisodeCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (FindEpisodeCard(sender) is LocalEpisodeCardViewModel card)
            {
                await PickCoverAndApplyAsync(bytes => ViewModel.Actions.ApplyEpisodeCoverFromBytesAsync(card, bytes));
            }
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Episode.
        /// </summary>
        private async void OnEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (FindEpisodeCard(sender) is LocalEpisodeCardViewModel card)
            {
                await SearchCoverAndApplyAsync(card.Title, hit => ViewModel.Actions.ApplySelectedEpisodeCoverAsync(card, hit));
            }
        }

        /// <summary>Die Serienkachel, deren Kontextmenü den Eintrag trägt; sonst null.</summary>
        private LocalArtistCardViewModel? FindSeriesCard(object sender)
            => sender is MenuFlyoutItem { Tag: Guid seriesId }
                ? ViewModel.ArtistsVM.Artists.FirstOrDefault(a => a.SeriesId == seriesId)
                : null;

        /// <summary>Die Folgenkachel, deren Kontextmenü den Eintrag trägt; sonst null.</summary>
        private LocalEpisodeCardViewModel? FindEpisodeCard(object sender)
            => sender is MenuFlyoutItem { Tag: Guid episodeId }
                ? ViewModel.EpisodesVM.Episodes.FirstOrDefault(ep => ep.EpisodeId == episodeId)
                : null;

        /// <summary>
        /// Lässt eine Bilddatei wählen und übergibt ihren Inhalt; ohne Auswahl geschieht nichts.
        /// </summary>
        /// <param name="apply">Übernimmt die Bilddaten als Cover.</param>
        /// <param name="operationName">Name der auslösenden Aktion für das Protokoll.</param>
        private Task PickCoverAndApplyAsync(Func<byte[], Task> apply, [CallerMemberName] string operationName = "")
            => AsyncEventHandler.RunSafelyAsync(async () =>
            {
                byte[]? bytes = await PickImageFileAsync();

                if (bytes is not null)
                {
                    await apply(bytes);
                }
            }, operationName);

        /// <summary>
        /// Öffnet die Cover-Suche mit dem Titel als Suchbegriff und übernimmt den gewählten Treffer.
        /// Im Offline-Modus wird vorher gefragt; ohne Zustimmung öffnet sich der Dialog nicht.
        /// </summary>
        /// <param name="title">Vorbelegter Suchbegriff.</param>
        /// <param name="apply">Übernimmt den gewählten Treffer als Cover.</param>
        /// <param name="operationName">Name der auslösenden Aktion für das Protokoll.</param>
        private Task SearchCoverAndApplyAsync(string title, Func<CoverSearchHit, Task> apply, [CallerMemberName] string operationName = "")
            => AsyncEventHandler.RunSafelyAsync(async () =>
            {
                using IDisposable? onlineScope = await ViewModel.Actions.RequestOnlineAccessForCoverSearchAsync();
                if (onlineScope is null)
                {
                    return;
                }

                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    title,
                    (query, page, ct) => ViewModel.Actions.SearchCoversAsync(query, page, ct),
                    Content.XamlRoot);

                if (selected is not null)
                {
                    await apply(selected);
                }
            }, operationName);
    }
}
