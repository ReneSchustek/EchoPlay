using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Veröffentlichungskalender im Speicher. Hält die geschriebenen Einträge und merkt sich
    /// Stichtag und Zeitpunkt des letzten vollständigen Aufbaus.
    /// </summary>
    internal sealed class FakeReleaseCalendarDataService : IReleaseCalendarDataService
    {
        private readonly Dictionary<long, ReleaseCalendarEntry> _entries = [];

        /// <summary>Die geschriebenen Einträge nach Sammlungs-Kennung.</summary>
        public IReadOnlyCollection<ReleaseCalendarEntry> Entries => _entries.Values;

        /// <summary>Ende des letzten vollständigen Aufbaus; vorbelegbar.</summary>
        public DateTime? LastRefresh { get; set; }

        /// <summary>Zahl der Schreibaufrufe.</summary>
        public int UpsertCallCount { get; private set; }

        /// <summary>Der zuletzt übergebene Stichtag zum Aufräumen.</summary>
        public DateTime? LastRemoveCutoff { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<ReleaseCalendarEntry>> GetBetweenAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReleaseCalendarEntry> result =
                [.. _entries.Values.Where(e => e.ReleaseDate >= fromUtc && e.ReleaseDate < toUtc).OrderBy(e => e.ReleaseDate)];
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<DateTime?> GetLatestReleaseDateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.Count == 0 ? (DateTime?)null : _entries.Values.Max(e => e.ReleaseDate));

        /// <inheritdoc />
        public Task<DateTime?> GetLastRefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LastRefresh);

        /// <inheritdoc />
        public Task SetLastRefreshAsync(DateTime refreshedAtUtc, CancellationToken cancellationToken = default)
        {
            LastRefresh = refreshedAtUtc;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpsertRangeAsync(IReadOnlyList<ReleaseCalendarEntry> entries, CancellationToken cancellationToken = default)
        {
            UpsertCallCount++;
            foreach (ReleaseCalendarEntry entry in entries)
            {
                _entries[entry.CollectionId] = entry;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
        {
            LastRemoveCutoff = cutoffUtc;
            List<long> expired = [.. _entries.Values.Where(e => e.ReleaseDate < cutoffUtc).Select(e => e.CollectionId)];
            foreach (long id in expired)
            {
                _ = _entries.Remove(id);
            }

            return Task.FromResult(expired.Count);
        }
    }
}
