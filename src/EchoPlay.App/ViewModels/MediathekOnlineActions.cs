using EchoPlay.App.Models;
using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Koordinator für die Async-Aktionen der Online-Mediathek. Hält vier Sub-Actions
    /// (<see cref="OnlineSeriesLoader"/>, <see cref="OnlineEpisodePipeline"/>,
    /// <see cref="OnlineProviderSearchActions"/>, <see cref="OnlineBulkRefreshActions"/>)
    /// und delegiert alle public Methoden an die passende Sub-Action. Das Top-VM
    /// <see cref="MediathekOnlineViewModel"/> hält nur noch Commands, Zustands-Properties
    /// und die Pass-Through-Schicht. Folgt dem Muster aus <see cref="DashboardDataLoader"/>
    /// und <see cref="TagManagerActions"/>.
    /// </summary>
    internal sealed class MediathekOnlineActions : IDisposable
    {
        private readonly OnlineActionsState _state = new();
        private readonly OnlineSeriesLoader _seriesLoader;
        private readonly OnlineEpisodePipeline _episodePipeline;
        private readonly OnlineProviderSearchActions _providerSearch;
        private readonly OnlineBulkRefreshActions _bulkRefresh;

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
            _seriesLoader = new OnlineSeriesLoader(
                context, seriesVM, _state,
                setIsLoading, setLoadingStatusText, setHasNoProvider);

            _episodePipeline = new OnlineEpisodePipeline(
                context, seriesVM, episodesVM, _state);

            _providerSearch = new OnlineProviderSearchActions(
                context, seriesVM, episodesVM, providerSearchVM,
                reloadAfterImportAsync);

            _bulkRefresh = new OnlineBulkRefreshActions(
                context, seriesVM, episodesVM,
                setIsLoading, setLoadingStatusText,
                reloadAfterRefreshAsync: LoadAsync);
        }

        /// <inheritdoc cref="OnlineSeriesLoader.LoadAsync"/>
        public Task LoadAsync() => _seriesLoader.LoadAsync();

        /// <inheritdoc cref="OnlineEpisodePipeline.SelectSeriesAsync"/>
        public Task SelectSeriesAsync(SeriesCardViewModel card) => _episodePipeline.SelectSeriesAsync(card);

        /// <inheritdoc cref="OnlineBulkRefreshActions.RemoveSeriesAsync"/>
        public async Task RemoveSeriesAsync(Guid seriesId)
        {
            using IDisposable ua = UserActionScope.BeginUserAction("RemoveOnlineSeries");
            await _bulkRefresh.RemoveSeriesAsync(seriesId);
        }

        /// <inheritdoc cref="OnlineBulkRefreshActions.ToggleWatchAsync"/>
        public async Task ToggleWatchAsync(Guid seriesId, bool watch)
        {
            using IDisposable ua = UserActionScope.BeginUserAction("ToggleOnlineWatch");
            await _bulkRefresh.ToggleWatchAsync(seriesId, watch);
        }

        /// <inheritdoc cref="OnlineProviderSearchActions.SearchProviderAsync"/>
        public async Task SearchProviderAsync(string searchText)
        {
            using IDisposable ua = UserActionScope.BeginUserAction("OnlineSearch");
            await _providerSearch.SearchProviderAsync(searchText);
        }

        /// <inheritdoc cref="OnlineProviderSearchActions.AddSelected"/>
        public void AddSelected() => _providerSearch.AddSelected();

        /// <inheritdoc cref="OnlineBulkRefreshActions.RefreshAllOnlineSeriesAsync"/>
        public async Task RefreshAllOnlineSeriesAsync()
        {
            using IDisposable ua = UserActionScope.BeginUserAction("RefreshAllOnlineSeries");
            await _bulkRefresh.RefreshAllOnlineSeriesAsync();
        }

        /// <inheritdoc cref="OnlineEpisodePipeline.SearchEpisodeCoversAsync"/>
        public static Task<IReadOnlyList<CoverSearchHit>> SearchEpisodeCoversAsync(
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearchService,
            string query,
            CancellationToken ct)
            => OnlineEpisodePipeline.SearchEpisodeCoversAsync(coverSearchService, query, ct);

        /// <inheritdoc cref="OnlineEpisodePipeline.ApplySelectedEpisodeCoverAsync"/>
        public async Task ApplySelectedEpisodeCoverAsync(OnlineEpisodeCardViewModel card, CoverSearchHit hit)
        {
            using IDisposable ua = UserActionScope.BeginUserAction("ApplyOnlineEpisodeCover");
            await _episodePipeline.ApplySelectedEpisodeCoverAsync(card, hit);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _episodePipeline.Dispose();
            _providerSearch.Dispose();
        }
    }
}
