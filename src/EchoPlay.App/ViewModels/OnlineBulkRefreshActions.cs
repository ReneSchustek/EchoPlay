using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Bulk-Operationen auf Online-Serien (Delta-Refresh aller,
    /// Serie entfernen, Watch-Toggle).
    /// </summary>
    internal sealed class OnlineBulkRefreshActions
    {
        private readonly MediathekOnlineActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setLoadingStatusText;
        private readonly Func<Task> _reloadAfterRefreshAsync;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int RefreshAllCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int RemoveSeriesCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ToggleWatchCallCount { get; private set; }

        public OnlineBulkRefreshActions(
            MediathekOnlineActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            Action<bool> setIsLoading,
            Action<string> setLoadingStatusText,
            Func<Task> reloadAfterRefreshAsync)
        {
            _ctx = context;
            _seriesVM = seriesVM;
            _episodesVM = episodesVM;
            _setIsLoading = setIsLoading;
            _setLoadingStatusText = setLoadingStatusText;
            _reloadAfterRefreshAsync = reloadAfterRefreshAsync;
        }

        /// <summary>
        /// Entfernt eine Online-Serie aus der Mediathek (Soft-Delete) nach Nutzerbestätigung.
        /// </summary>
        public async Task RemoveSeriesAsync(Guid seriesId)
        {
            RemoveSeriesCallCount++;

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
            ToggleWatchCallCount++;

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

        /// <summary>
        /// Prüft alle Online-Serien auf neue Folgen beim Provider (Delta-Update). Im
        /// Offline-Modus wird vorher der Online-Zugang angefragt; bei Ablehnung kehrt die
        /// Methode ohne Aktion zurück.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bulk-Refresh: Einzelserien-Fehler werden übersprungen, übergreifende Fehler landen als Dialog; Loading-State im finally geklärt.")]
        public async Task RefreshAllOnlineSeriesAsync()
        {
            RefreshAllCallCount++;

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
                await _reloadAfterRefreshAsync();
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
    }
}
