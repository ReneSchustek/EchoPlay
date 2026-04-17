using EchoPlay.App.Services;
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
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Lädt alle Online-Serien aus der DB, setzt <see cref="OnlineSeriesViewModel"/>
    /// und kümmert sich um das Hintergrund-Caching der Serien-Cover.
    /// Reicht abgeschlossene Episoden-IDs über <see cref="OnlineActionsState"/> an
    /// die Episoden-Pipeline durch.
    /// </summary>
    internal sealed class OnlineSeriesLoader
    {
        private readonly MediathekOnlineActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineActionsState _state;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setLoadingStatusText;
        private readonly Action<bool> _setHasNoProvider;

        /// <summary>Public Call-Counter für Tests (kein Zustand, nur Diagnose).</summary>
        public int LoadCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests: Hintergrund-Cover-Cache-Läufe.</summary>
        public int CacheCallCount { get; private set; }

        public OnlineSeriesLoader(
            MediathekOnlineActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineActionsState state,
            Action<bool> setIsLoading,
            Action<string> setLoadingStatusText,
            Action<bool> setHasNoProvider)
        {
            _ctx = context;
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Mediathek-Ladevorgang iteriert ueber abonnierte Serien: Import-/Provider-/DB-Fehler einer einzelnen Serie duerfen die Kachel-Erstellung der restlichen Serien nicht abbrechen.")]
        public async Task LoadAsync()
        {
            LoadCallCount++;
            _setIsLoading(true);

            try
            {
                using IServiceScope scope = _ctx.ScopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

                AppSettings settings = await settingsService.GetAsync();
                _setHasNoProvider(settings.ActiveProvider == ProviderType.None);
                bool hasNoProvider = settings.ActiveProvider == ProviderType.None;

                // Alle Wiedergabestände einmalig laden – für die Serien-Zähler und Episoden-Häkchen
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                _state.CompletedEpisodeIds = allStates
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
                        id: series.Id,
                        title: series.Title,
                        coverImage: await BuildCoverImageAsync(series),
                        totalEpisodeCount: episodes.Count,
                        newEpisodeCount: notStartedCount,
                        inProgressCount: inProgressCount,
                        finishedCount: finishedCount,
                        isSubscribed: series.IsSubscribed,
                        isFavorite: series.IsFavorite,
                        isWatched: series.IsWatched,
                        scopeFactory: _ctx.ScopeFactory,
                        confirmationDialogService: _ctx.ConfirmationDialogService,
                        localizationService: _ctx.LocalizationService));
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

        /// <summary>
        /// Lädt fehlende Serien-Cover von der Provider-URL herunter und speichert sie in der DB.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cache-Aufbau fuer Serien-Cover: HTTP-/IO-/DB-Fehler einer einzelnen Serie duerfen die Kachel-Ansicht nicht stoeren; Fehler werden lediglich geloggt.")]
        private async Task CacheSeriesCoversAsync(IReadOnlyList<Series> seriesList)
        {
            CacheCallCount++;

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
    }
}
