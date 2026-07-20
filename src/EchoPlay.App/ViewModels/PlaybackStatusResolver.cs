using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Playback;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Leitet den UI-<see cref="PlaybackStatus"/> aus einem gespeicherten <see cref="PlaybackState"/> ab.
    /// Zentralisiert die Ableitungsregel, die sonst in mehreren ViewModels dupliziert war.
    /// </summary>
    internal static class PlaybackStatusResolver
    {
        /// <summary>
        /// Bestimmt den Wiedergabestatus einer Episode aus ihrem gespeicherten Zustand.
        /// Kein Zustand oder Position Null → <see cref="PlaybackStatus.NotStarted"/>,
        /// abgeschlossen → <see cref="PlaybackStatus.Finished"/>, sonst <see cref="PlaybackStatus.InProgress"/>.
        /// </summary>
        /// <param name="state">Der gespeicherte Wiedergabezustand oder <c>null</c>.</param>
        /// <returns>Der abgeleitete Wiedergabestatus.</returns>
        public static PlaybackStatus Resolve(PlaybackState? state)
        {
            if (state is null || state.LastPosition == TimeSpan.Zero)
            {
                return PlaybackStatus.NotStarted;
            }

            return state.IsCompleted ? PlaybackStatus.Finished : PlaybackStatus.InProgress;
        }
    }
}
