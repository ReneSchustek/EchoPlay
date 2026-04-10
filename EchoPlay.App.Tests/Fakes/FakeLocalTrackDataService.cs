using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalTrackDataService"/>.
    /// Speichert <see cref="SaveTracksForEpisodeAsync"/>-Aufrufe und stellt Tracks für <see cref="GetByEpisodeIdAsync"/> bereit.
    /// </summary>
    internal sealed class FakeLocalTrackDataService : ILocalTrackDataService
    {
        private readonly Dictionary<Guid, IReadOnlyList<LocalTrack>> _tracksByEpisode;

        /// <summary>
        /// Erstellt den Fake, optional mit vorab konfigurierten Tracks.
        /// </summary>
        /// <param name="existingTracks">Vorab konfigurierte Tracks pro EpisodeId.</param>
        public FakeLocalTrackDataService(Dictionary<Guid, IReadOnlyList<LocalTrack>>? existingTracks = null)
        {
            _tracksByEpisode = existingTracks ?? [];
        }

        /// <summary>Alle gespeicherten Tracks nach EpisodeId.</summary>
        public Dictionary<Guid, IReadOnlyList<LocalTrack>> SavedTracks { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<LocalTrack>> GetByEpisodeIdAsync(Guid episodeId)
        {
            if (_tracksByEpisode.TryGetValue(episodeId, out IReadOnlyList<LocalTrack>? tracks))
            {
                return Task.FromResult(tracks);
            }

            return Task.FromResult<IReadOnlyList<LocalTrack>>([]);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, LocalTrack>> GetFirstTracksByEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds)
        {
            Dictionary<Guid, LocalTrack> result = [];
            foreach (Guid episodeId in episodeIds)
            {
                if (_tracksByEpisode.TryGetValue(episodeId, out IReadOnlyList<LocalTrack>? tracks)
                    && tracks.Count > 0)
                {
                    LocalTrack? first = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault();
                    if (first is not null)
                    {
                        result[episodeId] = first;
                    }
                }
            }
            return Task.FromResult<IReadOnlyDictionary<Guid, LocalTrack>>(result);
        }

        /// <inheritdoc/>
        public Task SaveTracksForEpisodeAsync(Guid episodeId, IReadOnlyList<LocalTrack> tracks)
        {
            SavedTracks[episodeId] = tracks;
            _tracksByEpisode[episodeId] = tracks;
            return Task.CompletedTask;
        }
    }
}
