using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Verwaltet die Serien-Auswahl, die Episoden-Pipeline (Cover-Kopie,
    /// Batch-Cover-Laden, Hintergrund-Nachladen) und die manuelle Episoden-Cover-Suche.
    /// Liest abgeschlossene Episoden-IDs aus <see cref="OnlineActionsState"/>.
    /// </summary>
    internal sealed class OnlineEpisodePipeline : IDisposable
    {
        private readonly MediathekOnlineActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly OnlineActionsState _state;

        // Bricht laufende Cover-Downloads ab, wenn eine andere Serie gewählt wird.
        private CancellationTokenSource? _episodeCoverCts;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SelectSeriesCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ApplyEpisodeCoverCallCount { get; private set; }

        public OnlineEpisodePipeline(
            MediathekOnlineActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            OnlineActionsState state)
        {
            _ctx = context;
            _seriesVM = seriesVM;
            _episodesVM = episodesVM;
            _state = state;
        }

        /// <summary>
        /// Wählt eine Serie, lädt ihre Episoden samt Cover-Pipeline und startet den
        /// Hintergrund-Download fehlender Episoden-Cover.
        /// </summary>
        public async Task SelectSeriesAsync(SeriesCardViewModel card)
        {
            ArgumentNullException.ThrowIfNull(card);
            SelectSeriesCallCount++;

            // Re-Klick auf bereits ausgewählte Kachel klappt das Akkordeon wieder zu (Toggle).
            // Laufende Cover-Downloads abbrechen, sonst landen Bytes in einem geschlossenen Panel.
            if (card.IsSelectedInAccordion)
            {
                if (_episodeCoverCts is not null)
                {
                    await _episodeCoverCts.CancelAsync();
                    _episodeCoverCts.Dispose();
                    _episodeCoverCts = null;
                }

                _seriesVM.DeselectSeries();
                _episodesVM.Clear();
                return;
            }

            // Laufende Cover-Downloads der vorherigen Serie abbrechen
            if (_episodeCoverCts is not null)
            {
                await _episodeCoverCts.CancelAsync();
                _episodeCoverCts.Dispose();
            }
            _episodeCoverCts = new CancellationTokenSource();
            CancellationToken ct = _episodeCoverCts.Token;

            // Alte Episoden sofort ausblenden, damit kein Spinner über alten Kacheln erscheint
            _episodesVM.Clear();

            _seriesVM.SelectSeries(card);
            _episodesVM.IsLoadingEpisodes = true;

            // Lokale Cover in CoverImages sicherstellen – liest cover.jpg / ID3-Tags aus dem Dateisystem
            if (_ctx.BackgroundCoverService is not null)
            {
                _ = await _ctx.BackgroundCoverService.EnsureLocalCoversForSeriesAsync(card.Title);
            }

            // Cover aus lokalen Episoden auf Online-Episoden kopieren (reine SQL-Operation, ms)
            using IServiceScope scope = _ctx.ScopeFactory.CreateScope();

            ICoverCopyService coverCopy = scope.ServiceProvider.GetRequiredService<ICoverCopyService>();
            _ = await coverCopy.CopyFromMatchingEpisodesAsync(card.Id);

            // Episoden aus DB laden – Cover sind jetzt schon gesetzt
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(card.Id);

            // Cover-Binärdaten per Batch laden – ein Query statt N Einzelzugriffe
            List<Guid> episodeIds = new(episodes.Count);
            foreach (Episode episode in episodes)
            {
                episodeIds.Add(episode.Id);
            }

            IReadOnlyDictionary<Guid, byte[]> coverMap = _ctx.CoverService is not null
                ? await _ctx.CoverService.GetEpisodeCoverBytesAsync(episodeIds)
                : new Dictionary<Guid, byte[]>();

            List<OnlineEpisodeCardViewModel> episodeCards = new(episodes.Count);

            foreach (Episode episode in episodes)
            {
                OnlineEpisodeCardViewModel episodeCard = new(
                    episodeId: episode.Id,
                    episodeNumber: episode.EpisodeNumber,
                    title: episode.Title,
                    releaseDate: episode.ReleaseDate,
                    isCompleted: _state.CompletedEpisodeIds.Contains(episode.Id),
                    providerUrl: episode.ProviderUrl,
                    scopeFactory: _ctx.ScopeFactory,
                    appleMusicAlbumId: episode.AppleMusicAlbumId,
                    spotifyAlbumId: episode.SpotifyAlbumId);

                if (coverMap.TryGetValue(episode.Id, out byte[]? coverData))
                {
                    BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverData);
                    if (coverImage is not null)
                    {
                        episodeCard.CoverImage = coverImage;
                    }
                }

                episodeCards.Add(episodeCard);
            }

            _episodesVM.SetEpisodes(episodeCards);
            _episodesVM.IsLoadingEpisodes = false;

            // Fehlende Cover im Hintergrund nachladen – UI zeigt erst Platzhalter,
            // Cover erscheinen progressiv sobald der Download fertig ist.
            bool hasMissingCovers = false;
            foreach (OnlineEpisodeCardViewModel ep in episodeCards)
            {
                if (ep.CoverImage is null)
                {
                    hasMissingCovers = true;
                    break;
                }
            }

            if (hasMissingCovers && _ctx.CoverCacheService is not null)
            {
                _ = RefreshMissingEpisodeCoversAsync(card.Id, episodeCards, ct);
            }
        }

        /// <summary>
        /// Lädt fehlende Episoden-Cover im Hintergrund herunter und aktualisiert die Kacheln
        /// progressiv. Wird abgebrochen, wenn der Nutzer eine andere Serie wählt.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nachlade-Task für fehlende Episoden-Cover im Hintergrund: HTTP-/Provider-/Cache-Fehler dürfen die UI nicht stören; Fehler werden geloggt.")]
        private async Task RefreshMissingEpisodeCoversAsync(
            Guid seriesId,
            List<OnlineEpisodeCardViewModel> episodeCards,
            CancellationToken ct)
        {
            try
            {
                Task cacheTask = _ctx.CoverCacheService!.CacheCoversAsync(seriesId, ct: ct);

                // Kacheln periodisch aktualisieren bis der Download fertig ist
                while (!cacheTask.IsCompleted && !ct.IsCancellationRequested)
                {
                    _ = await Task.WhenAny(cacheTask, Task.Delay(2000, ct));

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    await UpdateEpisodeCardsFromDbAsync(episodeCards);
                }

                await cacheTask;
                if (!ct.IsCancellationRequested)
                {
                    await UpdateEpisodeCardsFromDbAsync(episodeCards);
                }
            }
            catch (OperationCanceledException)
            {
                // Serienwechsel – erwarteter Abbruch
            }
            catch (Exception)
            {
                // Cover-Nachladen ist optional – kein Fehler für den Nutzer
            }
        }

        /// <summary>
        /// Lädt neu verfügbare Cover aus der DB und setzt sie auf den Kacheln.
        /// </summary>
        private async Task UpdateEpisodeCardsFromDbAsync(List<OnlineEpisodeCardViewModel> episodeCards)
        {
            List<Guid> missingIds = [];
            foreach (OnlineEpisodeCardViewModel epCard in episodeCards)
            {
                if (epCard.CoverImage is null)
                {
                    missingIds.Add(epCard.EpisodeId);
                }
            }

            if (missingIds.Count == 0)
            {
                return;
            }

            IReadOnlyDictionary<Guid, byte[]> coverMap = _ctx.CoverService is not null
                ? await _ctx.CoverService.GetEpisodeCoverBytesAsync(missingIds)
                : new Dictionary<Guid, byte[]>();

            foreach (OnlineEpisodeCardViewModel epCard in episodeCards)
            {
                if (epCard.CoverImage is not null)
                {
                    continue;
                }

                if (coverMap.TryGetValue(epCard.EpisodeId, out byte[]? coverData))
                {
                    BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverData);
                    if (coverImage is not null)
                    {
                        epCard.CoverImage = coverImage;
                    }
                }
            }
        }

        /// <summary>
        /// Sucht Cover-Kandidaten für einen Episoden-Titel über den Cover-Suchdienst und
        /// gibt die Treffer als App-eigene <see cref="CoverSearchHit"/>-Wrapper zurück.
        /// </summary>
        public static async Task<IReadOnlyList<CoverSearchHit>> SearchEpisodeCoversAsync(
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearchService,
            string query,
            CancellationToken ct)
        {
            if (coverSearchService is null)
            {
                return [];
            }

            IReadOnlyList<EchoPlay.LocalLibrary.Cover.CoverSearchResult> results =
                await coverSearchService.SearchAsync(query, ct);

            List<CoverSearchHit> hits = new(results.Count);
            foreach (EchoPlay.LocalLibrary.Cover.CoverSearchResult r in results)
            {
                hits.Add(CoverSearchHit.From(r));
            }
            return hits;
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter, speichert es über den <see cref="CoverService"/>
        /// und aktualisiert die Episodenkachel. Netzwerkfehler werden still verschluckt.
        /// </summary>
        public async Task ApplySelectedEpisodeCoverAsync(OnlineEpisodeCardViewModel card, CoverSearchHit hit)
        {
            ApplyEpisodeCoverCallCount++;

            try
            {
                HttpClient client = _ctx.HttpClientFactory.CreateClient("CoverDownload");
                byte[] coverBytes = await client.GetByteArrayAsync(new Uri(hit.FullUrl, UriKind.Absolute));
                await _ctx.CoverService.SetEpisodeCoverAsync(card.EpisodeId, coverBytes);

                BitmapImage? image = await CoverService.ConvertToBitmapAsync(coverBytes);
                if (image is not null)
                {
                    card.CoverImage = image;
                }
            }
            catch (HttpRequestException)
            {
                // Netzwerkfehler → Platzhalter bleibt
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _episodeCoverCts?.Cancel();
            _episodeCoverCts?.Dispose();
            _episodeCoverCts = null;
        }
    }
}
