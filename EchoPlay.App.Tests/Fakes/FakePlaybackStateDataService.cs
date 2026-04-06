using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IPlaybackStateDataService"/>.
    /// Gibt vorab konfigurierte <see cref="PlaybackState"/>-Instanzen zurück.
    /// Für <see cref="GetCountsBySeriesIdAsync"/> können Counts pro Serie vorkonfiguriert werden.
    /// </summary>
    internal sealed class FakePlaybackStateDataService : IPlaybackStateDataService
    {
        private readonly List<PlaybackState> _states;

        // Vorkonfigurierte Counts für GetCountsBySeriesIdAsync – nötig weil das
        // Fake keine Episode-zu-Serie-Zuordnung kennt und die Zählung nicht berechnen kann.
        private readonly IReadOnlyDictionary<Guid, (int Finished, int InProgress, int NotStarted)> _seriesCounts;

        /// <summary>
        /// Erstellt den Fake mit optionalen Wiedergabeständen und optionalen vorkonfigurierten Zählern pro Serie.
        /// </summary>
        /// <param name="states">Vorab konfigurierte Wiedergabestände.</param>
        /// <param name="seriesCounts">
        /// Vorkonfigurierte Zähler pro Serie-ID für <see cref="GetCountsBySeriesIdAsync"/>.
        /// Serien ohne Eintrag geben <c>(0, 0, 0)</c> zurück.
        /// </param>
        public FakePlaybackStateDataService(
            IEnumerable<PlaybackState>? states = null,
            IReadOnlyDictionary<Guid, (int Finished, int InProgress, int NotStarted)>? seriesCounts = null)
        {
            _states       = states?.ToList() ?? [];
            _seriesCounts = seriesCounts ?? new Dictionary<Guid, (int, int, int)>();
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PlaybackState>> GetAllAsync()
        {
            return Task.FromResult<IReadOnlyList<PlaybackState>>(_states.ToList());
        }

        /// <inheritdoc/>
        public Task<PlaybackState?> GetByEpisodeIdAsync(Guid episodeId)
        {
            PlaybackState? result = _states.FirstOrDefault(s => s.EpisodeId == episodeId);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task AddAsync(PlaybackState playbackState)
        {
            _states.Add(playbackState);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(PlaybackState playbackState)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeleteAsync(Guid id)
        {
            _states.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<HashSet<Guid>> GetCompletedEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds)
        {
            HashSet<Guid> completed = _states
                .Where(s => episodeIds.Contains(s.EpisodeId) && s.IsCompleted)
                .Select(s => s.EpisodeId)
                .ToHashSet();

            return Task.FromResult(completed);
        }

        /// <inheritdoc/>
        public Task<(int Finished, int InProgress, int NotStarted)> GetCountsBySeriesIdAsync(Guid seriesId)
        {
            (int Finished, int InProgress, int NotStarted) counts =
                _seriesCounts.TryGetValue(seriesId, out (int F, int I, int N) configured)
                    ? configured
                    : (0, 0, 0);

            return Task.FromResult(counts);
        }
    }
}
