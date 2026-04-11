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
    internal sealed class MediathekOnlineActions
    {
        // Wiederverwendbarer HTTP-Client für Cover-Downloads – static verhindert Socket-Erschöpfung
        private static readonly HttpClient _downloadClient = new();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly EpisodeCoverCacheService? _coverCacheService;
        private readonly CoverService _coverService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly IWatchToggleService? _watchToggleService;

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
        /// Initialisiert den Orchestrator mit allen Services, Sub-VMs und Zustands-Callbacks.
        /// </summary>
        public MediathekOnlineActions(
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IOnlineAccessGuard onlineAccessGuard,
            EpisodeCoverCacheService? coverCacheService,
            CoverService coverService,
            BackgroundCoverService? backgroundCoverService,
            IWatchToggleService? watchToggleService,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            OnlineProviderSearchViewModel providerSearchVM,
            Action<bool> setIsLoading,
            Action<string> setLoadingStatusText,
            Action<bool> setHasNoProvider,
            Func<Task> reloadAfterImportAsync)
        {
            _scopeFactory              = scopeFactory;
            _confirmationDialogService = confirmationDialogService;
            _importService             = importService;
            _errorDialogService        = errorDialogService;
            _localizationService       = localizationService;
            _onlineAccessGuard         = onlineAccessGuard;
            _coverCacheService         = coverCacheService;
            _coverService              = coverService;
            _backgroundCoverService    = backgroundCoverService;
            _watchToggleService        = watchToggleService;

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
        public async Task LoadAsync()
        {
            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
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
                            _localizationService.Get("OnlineReImportStatusText"), series.Title));

                        try
                        {
                            int reImported = await _importService.ReImportEpisodesAsync(series);

                            if (reImported > 0)
                            {
                                // Neuer Scope für die erneute Abfrage – der aktuelle Scope sieht
                                // die gerade per ImportService geschriebenen Daten evtl. nicht sofort
                                using IServiceScope freshScope = _scopeFactory.CreateScope();
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
                        scopeFactory:              _scopeFactory,
                        confirmationDialogService: _confirmationDialogService,
                        localizationService:       _localizationService));
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
            _episodeCoverCts?.Cancel();
            _episodeCoverCts?.Dispose();
            _episodeCoverCts = new CancellationTokenSource();
            CancellationToken ct = _episodeCoverCts.Token;

            // Alte Episoden sofort ausblenden, damit kein Spinner über alten Kacheln erscheint
            _episodesVM.Clear();

            _seriesVM.SelectSeries(card);
            _episodesVM.IsLoadingEpisodes = true;

            // Lokale Cover in CoverImages sicherstellen – liest cover.jpg / ID3-Tags aus dem Dateisystem
            if (_backgroundCoverService is not null)
            {
                await _backgroundCoverService.EnsureLocalCoversForSeriesAsync(card.Title);
            }

            // Cover aus lokalen Episoden auf Online-Episoden kopieren (reine SQL-Operation, ms)
            using IServiceScope scope = _scopeFactory.CreateScope();

            ICoverCopyService coverCopy = scope.ServiceProvider.GetRequiredService<ICoverCopyService>();
            await coverCopy.CopyFromMatchingEpisodesAsync(card.Id);

            // Episoden aus DB laden – Cover sind jetzt schon gesetzt
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(card.Id);

            // Cover-Binärdaten per Batch laden – ein Query statt N Einzelzugriffe
            List<Guid> episodeIds = new(episodes.Count);
            foreach (Episode episode in episodes)
            {
                episodeIds.Add(episode.Id);
            }

            IReadOnlyDictionary<Guid, byte[]> coverMap = _coverService is not null
                ? await _coverService.GetEpisodeCoverBytesAsync(episodeIds)
                : new Dictionary<Guid, byte[]>();

            List<OnlineEpisodeCardViewModel> episodeCards = new(episodes.Count);

            foreach (Episode episode in episodes)
            {
                OnlineEpisodeCardViewModel episodeCard = new(
                    episodeId:     episode.Id,
                    episodeNumber: episode.EpisodeNumber,
                    title:         episode.Title,
                    releaseDate:   episode.ReleaseDate,
                    isCompleted:   _completedEpisodeIds.Contains(episode.Id),
                    providerUrl:   episode.ProviderUrl,
                    scopeFactory:  _scopeFactory);

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

            if (hasMissingCovers && _coverCacheService is not null)
            {
                _ = RefreshMissingEpisodeCoversAsync(card.Id, episodeCards, ct);
            }
        }

        /// <summary>
        /// Lädt fehlende Episoden-Cover im Hintergrund herunter und aktualisiert die Kacheln
        /// progressiv. Wird abgebrochen, wenn der Nutzer eine andere Serie wählt.
        /// </summary>
        private async Task RefreshMissingEpisodeCoversAsync(
            Guid seriesId,
            List<OnlineEpisodeCardViewModel> episodeCards,
            CancellationToken ct)
        {
            try
            {
                Task cacheTask = _coverCacheService!.CacheCoversAsync(seriesId, ct: ct);

                // Kacheln periodisch aktualisieren bis der Download fertig ist
                while (!cacheTask.IsCompleted && !ct.IsCancellationRequested)
                {
                    await Task.WhenAny(cacheTask, Task.Delay(2000, ct));

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

            IReadOnlyDictionary<Guid, byte[]> coverMap = _coverService is not null
                ? await _coverService.GetEpisodeCoverBytesAsync(missingIds)
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

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _localizationService.Get("OnlineRemoveSeriesDialogTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _localizationService.Get("OnlineRemoveSeriesDialogMessage"),
                    card.Title));

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
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
            if (_watchToggleService is null)
            {
                return;
            }

            await _watchToggleService.ToggleAsync(seriesId, watch);

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
                    : await _importService.SearchAsync(searchText);
                IReadOnlyList<ImportSeries> albumResults = searchTypeIndex == 1
                    ? []
                    : await _importService.SearchAlbumsAsync(searchText);

                string searchLower = searchText.ToLowerInvariant();
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
                    bool alreadyImported = await _importService.IsAlreadyImportedAsync(series);
                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _importService, _errorDialogService,
                        _localizationService, onImportCompleted: _reloadAfterImportAsync));
                }

                _providerSearchVM.ProviderSearchResults = viewModels;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), ex.Message);
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
        public async Task RefreshAllOnlineSeriesAsync()
        {
            // Offline-Modus: Nutzer fragen, ob temporär online gegangen werden soll
            using IDisposable? onlineAccess = await _onlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineAccess is null)
            {
                return;
            }

            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
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
                        _localizationService.Get("OnlineRefreshProgressText"),
                        i + 1, onlineSeries.Count, series.Title));

                    try
                    {
                        await _importService.DeltaImportEpisodesAsync(series);
                    }
                    catch (Exception)
                    {
                        // Einzelne Serien-Fehler nicht abbrechen – nächste Serie prüfen
                    }

                    // Rate-Limiting: kurze Pause zwischen Provider-Aufrufen
                    if (i < onlineSeries.Count - 1)
                    {
                        await Task.Delay(1500);
                    }
                }

                _setLoadingStatusText(string.Empty);

                // Ansicht aktualisieren – neue Folgen sichtbar machen
                await LoadAsync();
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineRefreshFailedTitle"), ex.Message);
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
                byte[] coverBytes = await _downloadClient.GetByteArrayAsync(hit.FullUrl);
                await _coverService.SetEpisodeCoverAsync(card.EpisodeId, coverBytes);

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
        private async Task CacheSeriesCoversAsync(IReadOnlyList<Series> seriesList)
        {
            if (_coverService is null)
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

                bool hasCover = await _coverService.HasSeriesCoverAsync(series.Id);
                if (hasCover)
                {
                    continue;
                }

                try
                {
                    byte[] coverBytes = await _downloadClient.GetByteArrayAsync(series.CoverImageUrl);
                    if (coverBytes.Length == 0)
                    {
                        continue;
                    }

                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl);

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
            BitmapImage? coverImage = _coverService is not null
                ? await _coverService.GetSeriesCoverImageAsync(series.Id)
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
    }
}
