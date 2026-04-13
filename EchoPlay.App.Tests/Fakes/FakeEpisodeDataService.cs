using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        /// <inheritdoc/>
        public Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId)
        {
            IReadOnlyList<Episode> result = _episodes
                .Where(e => e.SeriesId == seriesId)
                .OrderBy(e => e.EpisodeNumber)
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds)
        {
            HashSet<Guid> idSet = new(seriesIds);
            IReadOnlyList<Episode> result = _episodes
                .Where(e => idSet.Contains(e.SeriesId))
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<Episode?> GetByIdAsync(Guid id)
        {
            Episode? result = _episodes.FirstOrDefault(e => e.Id == id);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task AddAsync(Episode episode)
        {
            // EF Core setzt die Id nach SaveChanges – hier per Reflection nachgebaut.
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            idProp.SetValue(episode, TestIds.Indexed(200 + _nextId++));
            _episodes.Add(episode);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddRangeAsync(IReadOnlyList<Episode> episodes)
        {
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            foreach (Episode episode in episodes)
            {
                idProp.SetValue(episode, TestIds.Indexed(200 + _nextId++));
                _episodes.Add(episode);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(Episode episode)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(
            IReadOnlyList<Guid> seriesIds)
        {
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
        public Task<IReadOnlyList<Episode>> GetMissingLocalEpisodesAsync(Guid seriesId)
        {
            IReadOnlyList<Episode> result = _episodes
                .Where(e => e.SeriesId == seriesId && e.LocalFolderPath is null)
                .OrderBy(e => e.EpisodeNumber)
                .ThenBy(e => e.Title)
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<int?> GetHighestLocalEpisodeNumberAsync(Guid seriesId)
        {
            int? result = _episodes
                .Where(e => e.SeriesId == seriesId && e.LocalFolderPath is not null && e.EpisodeNumber is not null)
                .Select(e => e.EpisodeNumber)
                .Max();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task DeleteAsync(Guid id)
        {
            _episodes.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetLocalCoverAsync(Guid episodeId, byte[]? coverData)
        {
            Episode? episode = _episodes.FirstOrDefault(e => e.Id == episodeId);

            if (episode is not null)
            {
                episode.LocalCoverData = coverData;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt)
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
