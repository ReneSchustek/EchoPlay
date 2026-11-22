using EchoPlay.Core.Http;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Lädt alle Online-Serien aus der DB, setzt <see cref="OnlineSeriesViewModel"/>
    /// und lädt die Serien-Cover im Hintergrund in den Zwischenspeicher.
    /// Reicht abgeschlossene Episoden-IDs über <see cref="OnlineActionsState"/> an
    /// die Episoden-Pipeline durch.
    /// </summary>
    internal sealed class OnlineSeriesLoader
    {
        private readonly OnlineLibraryActionsContext _ctx;
        private readonly EchoPlay.Logger.Abstractions.ILogger? _logger;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineActionsState _state;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setLoadingStatusText;
        private readonly Action<bool> _setHasNoProvider;

        /// <summary>Public Call-Counter für Tests (kein Zustand, nur Diagnose).</summary>
        public int LoadCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests: Hintergrund-Cover-Cache-Läufe.</summary>
        public int CacheCallCount { get; private set; }

        /// <summary>Richtet das Laden der Online-Serien ein.</summary>
        /// <param name="context">Die Dienste der Online-Mediathek.</param>
        /// <param name="seriesVM">Nimmt die Serienkacheln auf.</param>
        /// <param name="state">Teilt die gehörten Folgen mit der Folgen-Pipeline.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="setLoadingStatusText">Setzt den Statustext während des Ladens.</param>
        /// <param name="setHasNoProvider">Meldet, dass kein Anbieter eingerichtet ist.</param>
        public OnlineSeriesLoader(
            OnlineLibraryActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineActionsState state,
            Action<bool> setIsLoading,
            Action<string> setLoadingStatusText,
            Action<bool> setHasNoProvider)
        {
            _ctx = context;
            _logger = EchoPlay.App.Infrastructure.ScopedLogger.Create(context.ScopeFactory, nameof(OnlineSeriesLoader));
            _seriesVM = seriesVM;
            _state = state;
            _setIsLoading = setIsLoading;
            _setLoadingStatusText = setLoadingStatusText;
            _setHasNoProvider = setHasNoProvider;
        }

        /// <summary>
        /// Lädt alle online-importierten Serien, füllt <see cref="OnlineSeriesViewModel"/>
        /// und startet das Hintergrund-Cover-Caching.
        /// </summary>
        public async Task LoadAsync()
        {
            LoadCallCount++;
            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _ctx.ScopeFactory.CreateScope();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>().GetAsync();
                bool hasNoProvider = settings.ActiveProvider == ProviderType.None;
                _setHasNoProvider(hasNoProvider);

                // Alle Wiedergabestände einmalig laden, für die Serien-Zähler und Episoden-Häkchen
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                _state.CompletedEpisodeIds = allStates
                    .Where(s => s.IsCompleted)
                    .Select(s => s.EpisodeId)
                    .ToHashSet();

                IReadOnlyList<Series> dbSeries = await scope.ServiceProvider.GetRequiredService<ISeriesDataService>().GetAllAsync();
                _seriesVM.SetAllSeries(await BuildCardsAsync(dbSeries, episodeService, stateService, hasNoProvider));

                // Cover im Hintergrund herunterladen, die UI ist bereits sichtbar (Platzhalter),
                // Cover erscheinen progressiv sobald der Download fertig ist.
                _ = CacheSeriesCoversAsync(dbSeries);
            }
            finally
            {
                _setLoadingStatusText(string.Empty);
                _setIsLoading(false);
            }
        }

        /// <summary>Baut die Kacheln der online importierten Serien.</summary>
        private async Task<List<SeriesCardViewModel>> BuildCardsAsync(
            IReadOnlyList<Series> dbSeries, IEpisodeDataService episodeService, IPlaybackStateDataService stateService, bool hasNoProvider)
        {
            List<SeriesCardViewModel> cards = new(dbSeries.Count);
            foreach (Series series in dbSeries.Where(s => s.IsOnlineImported))
            {
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                // Nach einer Migration können Episoden fehlen, dann automatisch nachladen
                if (episodes.Count == 0 && !hasNoProvider)
                {
                    episodes = await ReImportEpisodesAsync(series) ?? episodes;
                }

                cards.Add(await BuildCardAsync(series, episodes.Count, stateService));
            }

            return cards;
        }

        /// <summary>
        /// Lädt die Folgen einer Serie beim Anbieter nach.
        /// </summary>
        /// <returns>Die Folgen danach, oder leer, wenn nichts nachgeladen wurde oder es scheiterte.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Mediathek-Ladevorgang iteriert über abonnierte Serien: Import-/Provider-/DB-Fehler einer einzelnen Serie dürfen die Kachel-Erstellung der restlichen Serien nicht abbrechen.")]
        private async Task<IReadOnlyList<Episode>?> ReImportEpisodesAsync(Series series)
        {
            _setLoadingStatusText(string.Format(
                CultureInfo.CurrentCulture,
                _ctx.LocalizationService.Get("OnlineReImportStatusText"), series.Title));

            try
            {
                int reImported = await _ctx.ImportService.ReImportEpisodesAsync(series);
                if (reImported == 0)
                {
                    return null;
                }

                // Neuer Scope für die erneute Abfrage, denn der aktuelle Scope sieht
                // die gerade per ImportService geschriebenen Daten evtl. nicht sofort
                using IServiceScope freshScope = _ctx.ScopeFactory.CreateScope();
                return await freshScope.ServiceProvider.GetRequiredService<IEpisodeDataService>().GetBySeriesIdAsync(series.Id);
            }
            catch (Exception ex)
            {
                // Ein Fehler bei einer Serie bricht den Ladevorgang der übrigen nicht ab.
                _logger?.Error("Folgen der Serie \"{Title}\" nicht geladen: {Reason}", ex, series.Title, ex.Message);
                return null;
            }
        }

        /// <summary>Baut die Kachel einer Serie mit ihren Zählern.</summary>
        private async Task<SeriesCardViewModel> BuildCardAsync(Series series, int episodeCount, IPlaybackStateDataService stateService)
        {
            (int finishedCount, int inProgressCount, int notStartedCount) =
                await stateService.GetCountsBySeriesIdAsync(series.Id);

            return new SeriesCardViewModel(
                new SeriesCardData
                {
                    Id = series.Id,
                    Title = series.Title,
                    TotalEpisodeCount = episodeCount,
                    NewEpisodeCount = notStartedCount,
                    InProgressCount = inProgressCount,
                    FinishedCount = finishedCount,
                    IsSubscribed = series.IsSubscribed,
                    IsFavorite = series.IsFavorite,
                    IsWatched = series.IsWatched,
                },
                coverImage: await BuildCoverImageAsync(series),
                scopeFactory: _ctx.ScopeFactory,
                confirmationDialogService: _ctx.ConfirmationDialogService,
                localizationService: _ctx.LocalizationService);
        }

        /// <summary>
        /// Lädt fehlende Serien-Cover von der Provider-URL herunter und speichert sie in der DB.
        /// </summary>
        /// <remarks>
        /// Läuft losgelöst; jede Serie steht deshalb ganz im eigenen Fang. Ein Fehler, der
        /// außerhalb davon entkäme, bliebe unbeobachtet und beendete das Nachladen aller übrigen.
        /// </remarks>
        private async Task CacheSeriesCoversAsync(IReadOnlyList<Series> seriesList)
        {
            CacheCallCount++;

            if (_ctx.CoverService is null)
            {
                return;
            }

            foreach (Series series in seriesList)
            {
                Uri? coverAddress = series.IsOnlineImported ? WebAddress.TryParse(series.CoverImageUrl) : null;
                if (coverAddress is not null)
                {
                    await CacheSeriesCoverAsync(_ctx.CoverService, series.Id, coverAddress);
                }
            }
        }

        /// <summary>
        /// Lädt das Cover einer Serie, wenn es noch fehlt, speichert es und setzt es auf die Kachel.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cache-Aufbau für Serien-Cover: IO-/DB-/Decoder-Fehler einer einzelnen Serie dürfen die Kachel-Ansicht nicht stören. HTTP-Fehler normalisiert bereits der ICoverDownloader.")]
        private async Task CacheSeriesCoverAsync(ICoverService coverService, Guid seriesId, Uri coverAddress)
        {
            try
            {
                if (await coverService.HasSeriesCoverAsync(seriesId))
                {
                    return;
                }

                byte[]? coverBytes = await _ctx.CoverDownloader.DownloadAsync(coverAddress);
                if (coverBytes is null)
                {
                    return;
                }

                await coverService.SetSeriesCoverAsync(seriesId, coverBytes, coverAddress);
                if (_seriesVM.AllSeries.FirstOrDefault(c => c.Id == seriesId) is { } card
                    && await CoverService.ConvertToBitmapAsync(coverBytes) is { } coverImage)
                {
                    card.CoverImage = coverImage;
                }
            }
            catch (Exception ex)
            {
                // Datenbankfehler oder ein Bild, das der Decoder nicht mag: Der Platzhalter bleibt.
                _logger?.Warning("Serien-Cover nicht übernommen: {Reason}", ex.Message);
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

            // Übergangsanzeige bis CacheSeriesCoversAsync das Cover in die DB geschrieben hat.
            // Dieselbe Prüfung wie beim Herunterladen: Eine kaputte oder fremde Adresse
            // (file://) darf weder werfen noch die Kachel etwas Lokales laden lassen.
            Uri? coverAddress = WebAddress.TryParse(series.CoverImageUrl);
            return coverAddress is not null ? new BitmapImage(coverAddress) : null;
        }
    }
}
