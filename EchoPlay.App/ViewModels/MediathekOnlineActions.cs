using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Interner Orchestrator für die Async-Aktionen der Online-Mediathek.
    /// Enthält Serien-Laden aus der DB, Episoden-Auswahl inklusive Cover-Pipeline,
    /// Provider-Suche, Batch-Import, Delta-Refresh und das Hintergrund-Caching der Cover.
    /// Das Top-VM <see cref="MediathekOnlineViewModel"/> hält nur noch Commands,
    /// Zustands-Properties und die Pass-Through-Schicht. Folgt dem Muster aus
    /// <see cref="DashboardDataLoader"/> und <see cref="TagManagerActions"/>.
    /// </summary>
    internal sealed class MediathekOnlineActions : IDisposable
    {
        private readonly MediathekOnlineActionsContext _ctx;

        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly OnlineProviderSearchViewModel _providerSearchVM;

        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setLoadingStatusText;
        private readonly Action<bool> _setHasNoProvider;
        private readonly Func<Task> _reloadAfterImportAsync;

        // Set mit IDs aller abgeschlossenen Episoden – wird beim Laden befüllt und beim
        // Anzeigen der Episoden-Häkchen verwendet.
        private HashSet<Guid> _completedEpisodeIds = [];

        // Bricht laufende Cover-Downloads ab, wenn eine andere Serie gewählt wird.
        private CancellationTokenSource? _episodeCoverCts;

        /// <summary>
        /// Initialisiert den Orchestrator mit dem Service-Context, Sub-VMs und Zustands-Callbacks.
        /// </summary>
        public MediathekOnlineActions(
            MediathekOnlineActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            OnlineProviderSearchViewModel providerSearchVM,
            Action<bool> setIsLoading,
            Action<string> setLoadingStatusText,
            Action<bool> setHasNoProvider,
            Func<Task> reloadAfterImportAsync)
        {
            _ctx = context;

            _seriesVM         = seriesVM;
            _episodesVM       = episodesVM;
            _providerSearchVM = providerSearchVM;

            _setIsLoading           = setIsLoading;
            _setLoadingStatusText   = setLoadingStatusText;
            _setHasNoProvider       = setHasNoProvider;
            _reloadAfterImportAsync = reloadAfterImportAsync;
        }

        // ── Laden der Serienliste ──────────────────────────────────────────────

        /// <summary>
        /// Lädt alle online-importierten Serien aus der Datenbank, füllt
        /// <see cref="OnlineSeriesViewModel"/> und startet das Hintergrund-Cover-Caching.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Mediathek-Ladevorgang iteriert ueber abonnierte Serien: Import-/Provider-/DB-Fehler einer einzelnen Serie duerfen die Kachel-Erstellung der restlichen Serien nicht abbrechen.")]
        public async Task LoadAsync()
        {
            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _ctx.ScopeFactory.CreateScope();
                ISeriesDataService        seriesService   = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService       episodeService  = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService    = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                IAppSettingsDataService   settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

                AppSettings settings = await settingsService.GetAsync();
                _setHasNoProvider(settings.ActiveProvider == ProviderType.None);
                bool hasNoProvider = settings.ActiveProvider == ProviderType.None;

                // Alle Wiedergabestände einmalig laden – für die Serien-Zähler und Episoden-Häkchen
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                _completedEpisodeIds = allStates
                    .Where(s => s.IsCompleted)
                    .Select(s => s.EpisodeId)
                    .ToHashSet();

                IReadOnlyList<Series> dbSeries = await seriesService.GetAllAsync();
                List<SeriesCardViewModel> cards = new(dbSeries.Count);

                foreach (Series series in dbSeries)
                {
                    if (!series.IsOnlineImported)
                    {
                        continue;
                    }

                    IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                    // Nach einer Migration können Episoden fehlen – automatisch nachladen
                    if (episodes.Count == 0 && !hasNoProvider)
                    {
                        _setLoadingStatusText(string.Format(
                            CultureInfo.CurrentCulture,
                            _ctx.LocalizationService.Get("OnlineReImportStatusText"), series.Title));

                        try
                        {
                            int reImported = await _ctx.ImportService.ReImportEpisodesAsync(series);

                            if (reImported > 0)
                            {
                                // Neuer Scope für die erneute Abfrage – der aktuelle Scope sieht
                                // die gerade per ImportService geschriebenen Daten evtl. nicht sofort
                                using IServiceScope freshScope = _ctx.ScopeFactory.CreateScope();
                                IEpisodeDataService freshEpisodeService = freshScope.ServiceProvider
                                    .GetRequiredService<IEpisodeDataService>();
                                episodes = await freshEpisodeService.GetBySeriesIdAsync(series.Id);
                            }
                        }
                        catch (Exception)
                        {
                            // Einzelne Serien-Fehler dürfen den Ladevorgang nicht abbrechen
                        }
                    }

                    (int finishedCount, int inProgressCount, int notStartedCount) =
                        await stateService.GetCountsBySeriesIdAsync(series.Id);

                    cards.Add(new SeriesCardViewModel(
                        id:                        series.Id,
                        title:                     series.Title,
                        coverImage:                await BuildCoverImageAsync(series),
                        totalEpisodeCount:         episodes.Count,
                        newEpisodeCount:           notStartedCount,
                        inProgressCount:           inProgressCount,
                        finishedCount:             finishedCount,
                        isSubscribed:              series.IsSubscribed,
                        isFavorite:                series.IsFavorite,
                        isWatched:                 series.IsWatched,
                        scopeFactory:              _ctx.ScopeFactory,
                        confirmationDialogService: _ctx.ConfirmationDialogService,
                        localizationService:       _ctx.LocalizationService));
                }

                _seriesVM.SetAllSeries(cards);

                // Cover im Hintergrund herunterladen – UI ist bereits sichtbar (Platzhalter),
                // Cover erscheinen progressiv sobald der Download fertig ist.
                _ = CacheSeriesCoversAsync(dbSeries);
            }
            finally
            {
                _setLoadingStatusText(string.Empty);
                _setIsLoading(false);
            }
        }

        // ── Serien-Auswahl + Episoden-Pipeline ─────────────────────────────────

        /// <summary>
        /// Wählt eine Serie im Akkordeon, lädt ihre Episoden samt Cover-Pipeline und startet
        /// den Hintergrund-Download fehlender Episoden-Cover. Laufende Downloads der
        /// vorherigen Auswahl werden abgebrochen.
        /// </summary>
        public async Task SelectSeriesAsync(SeriesCardViewModel card)
        {
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
                    episodeId:          episode.Id,
                    episodeNumber:      episode.EpisodeNumber,
                    title:              episode.Title,
                    releaseDate:        episode.ReleaseDate,
                    isCompleted:        _completedEpisodeIds.Contains(episode.Id),
                    providerUrl:        episode.ProviderUrl,
                    scopeFactory:       _ctx.ScopeFactory,
                    appleMusicAlbumId:  episode.AppleMusicAlbumId,
                    spotifyAlbumId:     episode.SpotifyAlbumId);

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nachlade-Task fuer fehlende Episoden-Cover im Hintergrund: HTTP-/Provider-/Cache-Fehler duerfen die UI nicht stoeren; Fehler werden geloggt.")]
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
        /// Lädt neu verfügbare Cover aus der DB und setzt sie auf den Kacheln. Nur Kacheln
        /// ohne Cover werden aktualisiert.
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

        // ── Serie entfernen + Überwachen ───────────────────────────────────────

        /// <summary>
        /// Entfernt eine Online-Serie aus der Mediathek (Soft-Delete) nach Nutzerbestätigung.
        /// </summary>
        public async Task RemoveSeriesAsync(Guid seriesId)
        {
            SeriesCardViewModel? card = null;
            foreach (SeriesCardViewModel c in _seriesVM.AllSeries)
            {
                if (c.Id == seriesId)
                {
                    card = c;
                    break;
                }
            }

            if (card is null)
            {
                return;
            }

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                _ctx.LocalizationService.Get("OnlineRemoveSeriesDialogTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _ctx.LocalizationService.Get("OnlineRemoveSeriesDialogMessage"),
                    card.Title));

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _ctx.ScopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            _seriesVM.RemoveSeries(seriesId);
            _episodesVM.Clear();
        }

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer Online-Serie um und aktualisiert die Kachel.
        /// </summary>
        public async Task ToggleWatchAsync(Guid seriesId, bool watch)
        {
            if (_ctx.WatchToggleService is null)
            {
                return;
            }

            await _ctx.WatchToggleService.ToggleAsync(seriesId, watch);

            SeriesCardViewModel? card = _seriesVM.Series.FirstOrDefault(c => c.Id == seriesId);
            if (card is not null)
            {
                card.IsWatched = watch;
            }
        }

        // ── Provider-Suche ─────────────────────────────────────────────────────

        /// <summary>
        /// Startet eine Provider-Suche mit dem übergebenen Suchtext und befüllt das
        /// <see cref="OnlineProviderSearchViewModel"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Provider-Suche in der Online-Mediathek: HTTP-/Parser-/Timeout-Fehler aus Spotify/AppleMusic werden als Nutzer-Fehlermeldung angezeigt, damit der Suche-Command nicht reisst.")]
        public async Task SearchProviderAsync(string searchText)
        {
            if (_providerSearchVM.IsSearchingProvider || string.IsNullOrWhiteSpace(searchText))
            {
                return;
            }

            // Offenes Akkordeon schließen – sonst überlagern sich Suchergebnisse und Folgen-Panel
            _seriesVM.DeselectSeries();
            _episodesVM.Clear();

            _providerSearchVM.IsSearchingProvider = true;
            _providerSearchVM.ProviderSearchResults = [];

            try
            {
                int searchTypeIndex = _providerSearchVM.SearchTypeIndex;

                IReadOnlyList<ImportSeries> seriesResults = searchTypeIndex == 2
                    ? []
                    : await _ctx.ImportService.SearchAsync(searchText);
                IReadOnlyList<ImportSeries> albumResults = searchTypeIndex == 1
                    ? []
                    : await _ctx.ImportService.SearchAlbumsAsync(searchText);

                string searchLower = searchText.ToUpperInvariant();
                List<ImportSeries> combined = new(seriesResults.Count + albumResults.Count);
                combined.AddRange(seriesResults);
                combined.AddRange(albumResults);
                combined.Sort((a, b) =>
                {
                    bool aContains = a.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (a.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);
                    bool bContains = b.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (b.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);

                    if (aContains != bContains)
                    {
                        return aContains ? -1 : 1;
                    }

                    return b.Score.CompareTo(a.Score);
                });

                List<SearchResultViewModel> viewModels = new(combined.Count);
                foreach (ImportSeries series in combined)
                {
                    bool alreadyImported = await _ctx.ImportService.IsAlreadyImportedAsync(series);
                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _ctx.ImportService, _ctx.ErrorDialogService,
                        _ctx.LocalizationService, _ctx.CoverBrightnessAnalyzer,
                        onImportCompleted: _reloadAfterImportAsync));
                }

                _providerSearchVM.ProviderSearchResults = viewModels;
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(
                    _ctx.LocalizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                _providerSearchVM.IsSearchingProvider = false;
            }
        }

        /// <summary>
        /// Importiert alle angehakten Suchergebnisse nacheinander. Bereits importierte
        /// Einträge werden übersprungen.
        /// </summary>
        public void AddSelected()
        {
            List<SearchResultViewModel> selected = [];
            foreach (SearchResultViewModel result in _providerSearchVM.ProviderSearchResults)
            {
                if (result.IsSelected && !result.IsImported)
                {
                    selected.Add(result);
                }
            }

            if (selected.Count == 0)
            {
                return;
            }

            foreach (SearchResultViewModel result in selected)
            {
                if (result.ImportCommand.CanExecute(null))
                {
                    result.ImportCommand.Execute(null);
                }
            }
        }

        // ── Delta-Refresh aller Online-Serien ──────────────────────────────────

        /// <summary>
        /// Prüft alle Online-Serien auf neue Folgen beim Provider (Delta-Update). Im
        /// Offline-Modus wird vorher der Online-Zugang angefragt; bei Ablehnung kehrt die
        /// Methode ohne Aktion zurück.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bulk-Refresh aller Online-Serien: Provider-/HTTP-/Parser-Fehler einer einzelnen Serie werden uebersprungen; ein uebergreifender Fehler wird dem Nutzer als Dialog angezeigt, damit der Command sauber beendet und der Loading-State im finally zurueckgesetzt werden kann.")]
        public async Task RefreshAllOnlineSeriesAsync()
        {
            // Offline-Modus: Nutzer fragen, ob temporär online gegangen werden soll
            using IDisposable? onlineAccess = await _ctx.OnlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineAccess is null)
            {
                return;
            }

            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _ctx.ScopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

                // Nur Online-Serien mit Provider-Zuordnung prüfen
                List<Series> onlineSeries = [];
                foreach (Series series in allSeries)
                {
                    if (series.IsOnlineImported)
                    {
                        onlineSeries.Add(series);
                    }
                }

                for (int i = 0; i < onlineSeries.Count; i++)
                {
                    Series series = onlineSeries[i];
                    _setLoadingStatusText(string.Format(
                        CultureInfo.CurrentCulture,
                        _ctx.LocalizationService.Get("OnlineRefreshProgressText"),
                        i + 1, onlineSeries.Count, series.Title));

                    try
                    {
                        _ = await _ctx.ImportService.DeltaImportEpisodesAsync(series);
                    }
                    catch (Exception)
                    {
                        // Einzelne Serien-Fehler nicht abbrechen – nächste Serie prüfen
                    }

                    // Rate-Limiting: drosselt aufeinanderfolgende Provider-Aufrufe
                    if (_ctx.RateLimiter is not null && i < onlineSeries.Count - 1)
                    {
                        await _ctx.RateLimiter.WaitAsync("itunes.apple.com");
                    }
                }

                _setLoadingStatusText(string.Empty);

                // Ansicht aktualisieren – neue Folgen sichtbar machen
                await LoadAsync();
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(
                    _ctx.LocalizationService.Get("OnlineRefreshFailedTitle"), ex.Message);
            }
            finally
            {
                _setLoadingStatusText(string.Empty);
                _setIsLoading(false);
            }
        }

        // ── Episoden-Cover-Suche (manueller Dialog) ─────────────────────────────

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

        // ── Hintergrund-Cover-Download ─────────────────────────────────────────

        /// <summary>
        /// Lädt fehlende Serien-Cover von der Provider-URL herunter und speichert sie in der DB.
        /// Läuft im Hintergrund nach <see cref="LoadAsync"/> – bereits gecachte Cover werden übersprungen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cache-Aufbau fuer Serien-Cover: HTTP-/IO-/DB-Fehler einer einzelnen Serie duerfen die Kachel-Ansicht nicht stoeren; Fehler werden lediglich geloggt.")]
        private async Task CacheSeriesCoversAsync(IReadOnlyList<Series> seriesList)
        {
            if (_ctx.CoverService is null)
            {
                return;
            }

            foreach (Series series in seriesList)
            {
                if (!series.IsOnlineImported)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(series.CoverImageUrl))
                {
                    continue;
                }

                bool hasCover = await _ctx.CoverService.HasSeriesCoverAsync(series.Id);
                if (hasCover)
                {
                    continue;
                }

                try
                {
                    HttpClient client = _ctx.HttpClientFactory.CreateClient("CoverDownload");
                    byte[] coverBytes = await client.GetByteArrayAsync(new Uri(series.CoverImageUrl, UriKind.Absolute));
                    if (coverBytes.Length == 0)
                    {
                        continue;
                    }

                    await _ctx.CoverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl);

                    SeriesCardViewModel? card = _seriesVM.AllSeries.FirstOrDefault(c => c.Id == series.Id);
                    if (card is not null)
                    {
                        BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverBytes);
                        if (coverImage is not null)
                        {
                            card.CoverImage = coverImage;
                        }
                    }
                }
                catch (Exception)
                {
                    // Netzwerkfehler oder abgelaufene URL → Platzhalter bleibt
                }
            }
        }

        /// <summary>
        /// Erstellt ein <see cref="BitmapImage"/> für eine Serie.
        /// Priorität: DB-Cover (CoverImages) → URL-Fallback (Übergangsanzeige).
        /// </summary>
        private async Task<BitmapImage?> BuildCoverImageAsync(Series series)
        {
            BitmapImage? coverImage = _ctx.CoverService is not null
                ? await _ctx.CoverService.GetSeriesCoverImageAsync(series.Id)
                : null;

            if (coverImage is not null)
            {
                return coverImage;
            }

            // Übergangsanzeige bis CacheSeriesCoversAsync das Cover in die DB geschrieben hat
            if (!string.IsNullOrEmpty(series.CoverImageUrl))
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            return null;
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
