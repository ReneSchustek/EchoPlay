using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
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
        private readonly OnlineLibraryActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly OnlineActionsState _state;

        // Bricht laufende Cover-Downloads ab, wenn eine andere Serie gewählt wird.
        private CancellationTokenSource? _episodeCoverCts;

        // Nur für die Laufzeitmessung der drei Schritte bis zur fertigen Liste.
        private readonly EchoPlay.Logger.Abstractions.ILogger? _logger;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SelectSeriesCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ApplyEpisodeCoverCallCount { get; private set; }

        public OnlineEpisodePipeline(
            OnlineLibraryActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            OnlineActionsState state)
        {
            _ctx = context;
            _seriesVM = seriesVM;
            _episodesVM = episodesVM;
            _state = state;

            // Der Protokollkanal wird einmalig beim Aufbau geholt, weil die Kontextklasse
            // keinen führt und der Bereich hier sofort wieder geschlossen wird.
            using IServiceScope loggerScope = context.ScopeFactory.CreateScope();
            _logger = loggerScope.ServiceProvider
                .GetService<EchoPlay.Logger.Abstractions.ILoggerFactory>()
                ?.CreateLogger("OnlineEpisodePipeline");
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

            // Die drei Schritte bis zur Liste werden einzeln gemessen. Ohne diese Zahlen ist
            // an einer trägen Serienansicht nicht zu erkennen, welcher davon sie träge macht.
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Lokale Cover in CoverImages sicherstellen – liest cover.jpg / ID3-Tags aus dem Dateisystem
            if (_ctx.BackgroundCoverService is not null)
            {
                _ = await _ctx.BackgroundCoverService.EnsureLocalCoversForSeriesAsync(card.Title);
            }

            long ensureMs = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            // Cover aus lokalen Episoden auf Online-Episoden kopieren (reine SQL-Operation, ms)
            using IServiceScope scope = _ctx.ScopeFactory.CreateScope();

            ICoverCopyService coverCopy = scope.ServiceProvider.GetRequiredService<ICoverCopyService>();
            _ = await coverCopy.CopyFromMatchingEpisodesAsync(card.Id);

            long copyMs = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            // Episoden aus DB laden – Cover sind jetzt schon gesetzt
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(card.Id);

            long episodesMs = stopwatch.ElapsedMilliseconds;
            _logger?.Info(
                "Serienansicht \"{Title}\": LokaleCover={EnsureMs} ms, CoverKopie={CopyMs} ms, Folgen={EpisodesMs} ms ({Count} Folgen)",
                card.Title, ensureMs, copyMs, episodesMs, episodes.Count);

            List<OnlineEpisodeCardViewModel> episodeCards = new(episodes.Count);

            foreach (Episode episode in episodes)
            {
                episodeCards.Add(new OnlineEpisodeCardViewModel(
                    episodeId: episode.Id,
                    episodeNumber: episode.EpisodeNumber,
                    title: episode.Title,
                    releaseDate: episode.ReleaseDate,
                    isCompleted: _state.CompletedEpisodeIds.Contains(episode.Id),
                    providerUrl: episode.ProviderUrl,
                    scopeFactory: _ctx.ScopeFactory,
                    appleMusicAlbumId: episode.AppleMusicAlbumId,
                    spotifyAlbumId: episode.SpotifyAlbumId));
            }

            // Nur die oberste Reihe bekommt ihr Bild sofort. Vorher alle zu holen hieße bei
            // „Bibi Blocksberg" (399 Folgen, rund 40 MB Bilddaten), dass die Liste erst nach
            // knapp zwanzig Sekunden überhaupt erscheint — gemessen, nicht geschätzt.
            stopwatch.Restart();
            await ApplyCoversAsync(episodeCards, episodes, 0, FirstVisibleCovers, ct);
            long firstCoversMs = stopwatch.ElapsedMilliseconds;

            stopwatch.Restart();
            _episodesVM.SetEpisodes(episodeCards);
            _episodesVM.IsLoadingEpisodes = false;
            long setMs = stopwatch.ElapsedMilliseconds;

            _logger?.Info(
                "Serienansicht \"{Title}\": ErsteCover={FirstCoversMs} ms, Liste setzen={SetMs} ms",
                card.Title, firstCoversMs, setMs);

            // Der Rest folgt in Chargen, während die Liste schon steht und bedienbar ist.
            if (episodeCards.Count > FirstVisibleCovers)
            {
                _ = ApplyRemainingCoversAsync(episodeCards, episodes, ct);
            }

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
        /// Wie viele Cover vor dem ersten Zeichnen umgewandelt werden. Das deckt den
        /// sichtbaren Bereich ab; alles Weitere kommt nach, während die Liste schon steht.
        /// </summary>
        private const int FirstVisibleCovers = 24;

        /// <summary>
        /// Größe einer Nachlade-Charge. Zwischen zwei Chargen kommt die Oberfläche wieder zum
        /// Zeichnen — ohne diese Pause bliebe sie bis zum letzten Bild stehen.
        /// </summary>
        private const int CoverBatchSize = 40;

        /// <summary>
        /// Wartezeit, bevor das Nachtragen beginnt. Sie gehört der Liste: Erst wenn sie
        /// gezeichnet ist, darf um denselben Faden gerungen werden.
        /// </summary>
        private static readonly TimeSpan FirstDrawPause = TimeSpan.FromMilliseconds(400);

        /// <summary>Pause zwischen zwei Chargen, damit die Oberfläche zum Zeichnen kommt.</summary>
        private static readonly TimeSpan CoverBatchPause = TimeSpan.FromMilliseconds(30);

        /// <summary>
        /// Zielbreite der Kachelbilder. Die Kachel ist 120 Punkte breit; das Doppelte hält sie
        /// auch auf einem hoch aufgelösten Bildschirm scharf und dekodiert trotzdem nur einen
        /// Bruchteil der 600 Punkte, die in der Datenbank liegen.
        /// </summary>
        private const int TileDecodeWidth = 240;

        /// <summary>
        /// Wandelt die Bilddaten einer Spanne in Bildobjekte um und setzt sie auf die Kacheln.
        /// </summary>
        /// <param name="cards">Die Kacheln in derselben Reihenfolge wie die Folgen.</param>
        /// <param name="episodes">Die Folgen, aus denen die Kennungen kommen.</param>
        /// <param name="start">Erster Eintrag der Spanne.</param>
        /// <param name="count">Wie viele Einträge höchstens.</param>
        /// <param name="ct">Endet, sobald eine andere Serie gewählt wird.</param>
        private async Task ApplyCoversAsync(
            List<OnlineEpisodeCardViewModel> cards,
            IReadOnlyList<Episode> episodes,
            int start,
            int count,
            CancellationToken ct)
        {
            if (_ctx.CoverService is null) return;

            int ende = Math.Min(start + count, cards.Count);
            if (ende <= start) return;

            // Die Bilddaten dieser Spanne in einem Zug — ein Abruf je Charge statt einem für
            // den ganzen Bestand.
            List<Guid> episodeIds = new(ende - start);
            for (int i = start; i < ende; i++)
            {
                episodeIds.Add(episodes[i].Id);
            }

            IReadOnlyDictionary<Guid, byte[]> coverMap =
                await _ctx.CoverService.GetEpisodeCoverBytesAsync(episodeIds, ct);

            for (int i = start; i < ende; i++)
            {
                if (ct.IsCancellationRequested) return;

                if (!coverMap.TryGetValue(episodes[i].Id, out byte[]? coverData)) continue;

                BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(
                    coverData, TileDecodeWidth, ct);
                if (coverImage is not null)
                {
                    cards[i].CoverImage = coverImage;
                }
            }
        }

        /// <summary>
        /// Trägt die übrigen Cover chargenweise nach, nachdem die Liste bereits steht.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nachtragen der übrigen Cover: Fehler beim Umwandeln einzelner Bilder dürfen die Liste nicht stören; die betroffene Kachel behält ihren Platzhalter.")]
        private async Task ApplyRemainingCoversAsync(
            List<OnlineEpisodeCardViewModel> cards,
            IReadOnlyList<Episode> episodes,
            CancellationToken ct)
        {
            try
            {
                // Der Liste den Vortritt lassen: Erst wenn sie gezeichnet ist, beginnt das
                // Nachtragen. Ohne diese Pause kämpfen beide um denselben Faden, und die
                // Kacheln erscheinen später als ohne jedes Nachladen.
                await Task.Delay(FirstDrawPause, ct);

                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

                for (int offset = FirstVisibleCovers; offset < cards.Count; offset += CoverBatchSize)
                {
                    if (ct.IsCancellationRequested) return;

                    await ApplyCoversAsync(cards, episodes, offset, CoverBatchSize, ct);

                    // Die Oberfläche kommt zwischen zwei Chargen wieder zum Zeichnen.
                    await Task.Delay(CoverBatchPause, ct);
                }

                _logger?.Info(
                    "Serienansicht: {Count} übrige Cover in {ElapsedMs} ms nachgetragen.",
                    cards.Count - FirstVisibleCovers, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                // Serienwechsel — erwarteter Abbruch.
            }
            catch (Exception)
            {
                // Cover sind Beiwerk; die Liste bleibt bedienbar.
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
            EchoPlay.LocalLibrary.Cover.CoverSearchPage page,
            CancellationToken ct)
        {
            if (coverSearchService is null)
            {
                return [];
            }

            IReadOnlyList<EchoPlay.LocalLibrary.Cover.CoverSearchResult> results =
                await coverSearchService.SearchAsync(query, page, ct);

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

            // Netzwerkfehler ergeben null — dann bleibt der Platzhalter stehen.
            byte[]? coverBytes = await _ctx.CoverDownloader.DownloadAsync(hit.FullUrl);
            if (coverBytes is null)
            {
                return;
            }

            await _ctx.CoverService.SetEpisodeCoverAsync(card.EpisodeId, coverBytes);

            BitmapImage? image = await CoverService.ConvertToBitmapAsync(coverBytes);
            if (image is not null)
            {
                card.CoverImage = image;
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
