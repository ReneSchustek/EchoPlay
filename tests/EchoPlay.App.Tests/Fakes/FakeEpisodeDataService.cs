using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IEpisodeDataService"/>.
    /// Verwendet eine In-Memory-Liste als Datenspeicher.
    /// </summary>
    internal sealed class FakeEpisodeDataService : IEpisodeDataService
    {
        private readonly List<Episode> _episodes = [];
        private int _nextId;

        /// <summary>Alle bisher gespeicherten Episoden.</summary>
        public IReadOnlyList<Episode> All => _episodes;

        /// <summary>Zähler für Aufrufe von <see cref="GetByIdAsync"/> für N+1-Detektion in Tests.</summary>
        public int GetByIdAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="GetByIdsAsync"/> für Batch-Verifikation (Single-Roundtrip statt N Aufrufe).</summary>
        public int GetByIdsAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="AddAsync"/> für N+1-Detektion in Tests.</summary>
        public int AddAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="AddRangeAsync"/> für Batch-Verifikation (Single-Roundtrip statt N Aufrufe).</summary>
        public int AddRangeAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="UpdateAsync"/> für N+1-Detektion in Tests.</summary>
        public int UpdateAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="UpdateRangeAsync"/> für Batch-Verifikation (Single-Roundtrip statt N Aufrufe).</summary>
        public int UpdateRangeAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="GetBySeriesIdAsync"/> für N+1-Detektion (StatistikViewModel etc.).</summary>
        public int GetBySeriesIdAsyncCallCount { get; private set; }

        /// <summary>Zähler für Aufrufe von <see cref="GetEpisodeCountsForSeriesAsync"/> für Batch-Verifikation.</summary>
        public int GetEpisodeCountsForSeriesAsyncCallCount { get; private set; }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            GetBySeriesIdAsyncCallCount++;
            IReadOnlyList<Episode> result = _episodes
                .Where(e => e.SeriesId == seriesId)
                .OrderBy(e => e.EpisodeNumber)
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            HashSet<Guid> idSet = new(seriesIds);
            IReadOnlyList<Episode> result = _episodes
                .Where(e => idSet.Contains(e.SeriesId))
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<Episode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetByIdAsyncCallCount++;
            Episode? result = _episodes.FirstOrDefault(e => e.Id == id);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, Episode>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
        {
            GetByIdsAsyncCallCount++;
            HashSet<Guid> idSet = new(ids);
            Dictionary<Guid, Episode> result = _episodes
                .Where(e => idSet.Contains(e.Id))
                .ToDictionary(e => e.Id);
            IReadOnlyDictionary<Guid, Episode> readOnly = result;
            return Task.FromResult(readOnly);
        }

        /// <inheritdoc/>
        public Task AddAsync(Episode episode, CancellationToken cancellationToken = default)
        {
            AddAsyncCallCount++;
            // EF Core setzt die Id nach SaveChanges – hier per Reflection nachgebaut.
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            idProp.SetValue(episode, TestIds.Indexed(200 + _nextId++));
            _episodes.Add(episode);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default)
        {
            AddRangeAsyncCallCount++;
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            foreach (Episode episode in episodes)
            {
                // BaseEntity-Konstruktor liefert bereits eine Guid; nur überschreiben, wenn noch leer.
                if (episode.Id == Guid.Empty)
                {
                    idProp.SetValue(episode, TestIds.Indexed(200 + _nextId++));
                }
                _episodes.Add(episode);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(Episode episode, CancellationToken cancellationToken = default)
        {
            UpdateAsyncCallCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default)
        {
            UpdateRangeAsyncCallCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(
            IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            GetEpisodeCountsForSeriesAsyncCallCount++;
            Dictionary<Guid, (int Total, int Local)> result = new();

            foreach (Guid seriesId in seriesIds)
            {
                int total = _episodes.Count(e => e.SeriesId == seriesId);
                int local = _episodes.Count(e => e.SeriesId == seriesId && e.LocalFolderPath is not null);
                result[seriesId] = (total, local);
            }

            IReadOnlyDictionary<Guid, (int Total, int Local)> readOnly = result;
            return Task.FromResult(readOnly);
        }

        /// <inheritdoc/>
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _ = _episodes.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt, CancellationToken cancellationToken = default)
        {
            Episode? episode = _episodes.FirstOrDefault(e => e.Id == episodeId);

            if (episode is not null)
            {
                episode.CoverLastChecked = checkedAt;
            }

            return Task.CompletedTask;
        }
    }
}
