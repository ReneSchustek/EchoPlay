using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was der Abgleich einer Neuerscheinung mit dem lokalen Bestand ergeben hat.
    /// </summary>
    /// <param name="EpisodeId">Die Kennung der lokalen Folge, oder <see cref="Guid.Empty"/>.</param>
    /// <param name="HasLocalTrack">Ob mindestens eine Datei dazu vorliegt.</param>
    /// <param name="IsCompleted">Ob die Folge bereits vollständig gehört ist.</param>
    internal readonly record struct LocalEpisodeMatch(Guid EpisodeId, bool HasLocalTrack, bool IsCompleted);

    /// <summary>
    /// Der lokale Bestand für den Abgleich der Neuerscheinungen: die Wiedergabestände
    /// einmal vollständig, die Folgenlisten je Serie beim ersten Zugriff.
    /// </summary>
    /// <remarks>
    /// Beides verhindert eine Abfrage je Eintrag. Ohne diesen Zwischenschritt löste eine
    /// Liste mit hundert Neuerscheinungen hundert Abfragen aus — und die Startseite wartete
    /// darauf, bevor sie den Abschnitt zeigen konnte.
    /// </remarks>
    internal sealed class LocalEpisodeIndex
    {
        private readonly IEpisodeDataService _episodeService;
        private readonly Dictionary<Guid, PlaybackState> _stateByEpisodeId;
        private readonly Dictionary<Guid, IReadOnlyList<Episode>> _episodesBySeries = [];

        private LocalEpisodeIndex(IEpisodeDataService episodeService, Dictionary<Guid, PlaybackState> stateByEpisodeId)
        {
            _episodeService = episodeService;
            _stateByEpisodeId = stateByEpisodeId;
        }

        /// <summary>
        /// Liest die Wiedergabestände einmal vollständig ein.
        /// </summary>
        /// <param name="episodeService">Zugriff auf die Folgen.</param>
        /// <param name="stateService">Zugriff auf die Wiedergabestände.</param>
        /// <param name="cancellationToken">Bricht das Einlesen ab.</param>
        /// <returns>Der einsatzbereite Bestandsindex.</returns>
        public static async Task<LocalEpisodeIndex> CreateAsync(
            IEpisodeDataService episodeService,
            IPlaybackStateDataService stateService,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync(cancellationToken);
            Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
            foreach (PlaybackState ps in allStates)
            {
                stateByEpisodeId[ps.EpisodeId] = ps;
            }

            return new LocalEpisodeIndex(episodeService, stateByEpisodeId);
        }

        /// <summary>
        /// Sucht die lokale Folge zu einer Folgennummer und liest ihren Hörstand.
        /// </summary>
        /// <param name="seriesId">Die Serie, in der gesucht wird.</param>
        /// <param name="episodeNumber">Die Folgennummer aus dem Eintrag des Anbieters.</param>
        /// <param name="cancellationToken">Bricht das Nachladen der Folgenliste ab.</param>
        /// <returns>Das Ergebnis des Abgleichs; ohne Treffer sind alle Angaben leer.</returns>
        public async Task<LocalEpisodeMatch> MatchAsync(Guid seriesId, int? episodeNumber, CancellationToken cancellationToken)
        {
            if (!episodeNumber.HasValue)
            {
                return default;
            }

            if (!_episodesBySeries.TryGetValue(seriesId, out IReadOnlyList<Episode>? localEpisodes))
            {
                localEpisodes = await _episodeService.GetBySeriesIdAsync(seriesId, cancellationToken);
                _episodesBySeries[seriesId] = localEpisodes;
            }

            foreach (Episode ep in localEpisodes)
            {
                if (ep.EpisodeNumber == episodeNumber.Value)
                {
                    bool isCompleted = _stateByEpisodeId.TryGetValue(ep.Id, out PlaybackState? state) && state.IsCompleted;
                    return new LocalEpisodeMatch(ep.Id, ep.LocalTrackCount is > 0, isCompleted);
                }
            }

            return default;
        }
    }
}
