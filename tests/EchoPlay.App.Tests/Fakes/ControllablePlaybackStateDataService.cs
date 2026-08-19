using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Data.Services.Projections;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IPlaybackStateDataService"/>, dessen Lesezugriff sich anhalten und
    /// dessen Schreibzugriff sich scheitern lässt.
    /// </summary>
    /// <remarks>
    /// Anders als <see cref="FakePlaybackStateDataService"/>, der einfach antwortet. Für den
    /// Hörstand zählen zwei Fälle, die man ohne Steuerung nicht deterministisch trifft: ein
    /// zweiter Schreibversuch, während der erste noch läuft, und ein Schreibvorgang, der
    /// scheitert, ohne die Wiedergabe zu stören. Angehalten wird über
    /// <see cref="TaskCompletionSource"/> statt über Wartezeiten — sonst hinge der Test an
    /// der Uhr statt an der Sache.
    /// </remarks>
    internal sealed class ControllablePlaybackStateDataService : IPlaybackStateDataService
    {
        private readonly Dictionary<Guid, PlaybackState> _states = [];
        private TaskCompletionSource? _readGate;

        /// <summary>Wie oft ein neuer Hörstand angelegt wurde.</summary>
        public int AddCallCount { get; private set; }

        /// <summary>Wie oft ein vorhandener Hörstand fortgeschrieben wurde.</summary>
        public int UpdateCallCount { get; private set; }

        /// <summary>Wie oft überhaupt gelesen wurde — auch das zählt als begonnener Versuch.</summary>
        public int ReadCallCount { get; private set; }

        /// <summary>Ist gesetzt, scheitert jeder Schreibvorgang mit dieser Meldung.</summary>
        public string? FailWriteWith { get; set; }

        /// <summary>Legt einen Hörstand vor, den der Schreiber vorfinden soll.</summary>
        public void Seed(PlaybackState state) => _states[state.EpisodeId] = state;

        /// <summary>Hält den nächsten Lesezugriff an, bis <see cref="ReleaseRead"/> gerufen wird.</summary>
        public void BlockRead() => _readGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gibt den angehaltenen Lesezugriff frei.</summary>
        public void ReleaseRead() => _readGate?.TrySetResult();

        /// <inheritdoc/>
        public async Task<PlaybackState?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            ReadCallCount++;

            if (_readGate is not null)
            {
                await _readGate.Task;
            }

            _ = _states.TryGetValue(episodeId, out PlaybackState? state);
            return state;
        }

        /// <inheritdoc/>
        public Task AddAsync(PlaybackState playbackState, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playbackState);

            if (FailWriteWith is not null)
            {
                return Task.FromException(new InvalidOperationException(FailWriteWith));
            }

            AddCallCount++;
            _states[playbackState.EpisodeId] = playbackState;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(PlaybackState playbackState, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playbackState);

            if (FailWriteWith is not null)
            {
                return Task.FromException(new InvalidOperationException(FailWriteWith));
            }

            UpdateCallCount++;
            _states[playbackState.EpisodeId] = playbackState;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PlaybackState>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlaybackState>>([.. _states.Values]);

        /// <inheritdoc/>
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task MarkCompletedAsync(Guid episodeId, DateTime completedAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <inheritdoc/>
        public Task MarkNotStartedAsync(Guid episodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task<int> SynchronizeCompletionAcrossSourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        /// <inheritdoc/>
        public Task<HashSet<Guid>> GetCompletedEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new HashSet<Guid>());

        /// <inheritdoc/>
        public Task<(int Finished, int InProgress, int NotStarted)> GetCountsBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0));

        /// <inheritdoc/>
        public Task<IReadOnlyList<RecentPlaybackRow>> GetRecentActiveAsync(int maxRows, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RecentPlaybackRow>>([]);
    }
}
