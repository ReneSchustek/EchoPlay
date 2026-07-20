using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet die Wiedergabe einer lokalen Episode: lädt die Tracks, berechnet die
    /// Fortsetzungsposition aus dem gespeicherten Wiedergabestand und übergibt sie an den
    /// <see cref="IPlayerService"/>. Bündelt die sonst in mehreren ViewModels wiederholte Sequenz.
    /// </summary>
    internal static class PlaybackLauncher
    {
        /// <summary>
        /// Lädt die Tracks der Episode und startet die Wiedergabe – fortgesetzt an der zuletzt
        /// gespeicherten Position, sofern die Episode noch nicht abgeschlossen ist.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="playerService">Der Player-Service, der die Wiedergabe startet.</param>
        /// <param name="episodeId">Die ID der abzuspielenden Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public static async Task PlayEpisodeAsync(
            IServiceScopeFactory scopeFactory,
            IPlayerService playerService,
            Guid episodeId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(playerService);

            using IServiceScope scope = scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episodeId, cancellationToken);

            if (tracks.Count == 0)
            {
                return;
            }

            PlaybackState? savedState = await stateService.GetByEpisodeIdAsync(episodeId, cancellationToken);

            // Nur fortsetzen, wenn die Episode nicht bereits abgeschlossen ist
            TimeSpan resumePosition = savedState is { IsCompleted: false } ? savedState.LastPosition : TimeSpan.Zero;

            List<string> paths = new(tracks.Count);
            foreach (LocalTrack track in tracks)
            {
                paths.Add(track.FilePath);
            }

            playerService.Play(episodeId, paths, startIndex: 0, resumePosition: resumePosition);
        }
    }
}
