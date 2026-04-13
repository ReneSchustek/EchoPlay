using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// In-Memory-Fake für <see cref="ICachedNewReleaseDataService"/>.
    /// Speichert Einträge in einer Liste, ohne Datenbankzugriff.
    /// </summary>
    internal sealed class FakeCachedNewReleaseDataService : ICachedNewReleaseDataService
    {
        private readonly List<CachedNewRelease> _entries = [];

        /// <summary>
        /// Optionaler Konstruktor mit vorbefüllten Einträgen für Tests,
        /// die einen gefüllten Cache benötigen.
        /// </summary>
        /// <param name="entries">Initiale Cache-Einträge.</param>
        public FakeCachedNewReleaseDataService(IReadOnlyList<CachedNewRelease>? entries = null)
        {
            if (entries is not null)
            {
                _entries.AddRange(entries);
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CachedNewRelease>> GetAllAsync()
        {
            IReadOnlyList<CachedNewRelease> result = _entries
                .OrderByDescending(c => c.ReleaseDate)
                .ToList();

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CachedNewRelease>> GetBySeriesIdAsync(Guid seriesId)
        {
            IReadOnlyList<CachedNewRelease> result = _entries
                .Where(c => c.SeriesId == seriesId)
                .OrderByDescending(c => c.ReleaseDate)
                .ToList();

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<DateTime?> GetLatestCheckTimeAsync()
        {
            DateTime? latest = _entries.Count > 0
                ? _entries.Max(c => c.CheckedAtUtc)
                : null;

            return Task.FromResult(latest);
        }

        /// <inheritdoc />
        public Task UpsertRangeAsync(IReadOnlyList<CachedNewRelease> entries)
        {
            foreach (CachedNewRelease entry in entries)
            {
                CachedNewRelease? existing = _entries.FirstOrDefault(c => c.CollectionId == entry.CollectionId);
                if (existing is not null)
                {
                    existing.Title = entry.Title;
                    existing.EpisodeNumber = entry.EpisodeNumber;
                    existing.ReleaseDate = entry.ReleaseDate;
                    existing.CoverUrl = entry.CoverUrl;
                    existing.CheckedAtUtc = entry.CheckedAtUtc;
                }
                else
                {
                    _entries.Add(entry);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> RemoveOlderThanAsync(DateTime cutoff)
        {
            int removed = _entries.RemoveAll(c => c.ReleaseDate < cutoff);
            return Task.FromResult(removed);
        }

        /// <inheritdoc />
        public Task<int> RemoveBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds)
        {
            int removed = _entries.RemoveAll(c => seriesIds.Contains(c.SeriesId));
            return Task.FromResult(removed);
        }

        /// <inheritdoc />
        public Task ClearAllAsync()
        {
            _entries.Clear();
            return Task.CompletedTask;
        }
    }
}
