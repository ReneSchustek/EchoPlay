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
        /// Kein Zustand → <see cref="PlaybackStatus.NotStarted"/>, abgeschlossen →
        /// <see cref="PlaybackStatus.Finished"/>, Position gesetzt →
        /// <see cref="PlaybackStatus.InProgress"/>.
        /// </summary>
        /// <param name="state">Der gespeicherte Wiedergabezustand oder <c>null</c>.</param>
        /// <returns>Der abgeleitete Wiedergabestatus.</returns>
        public static PlaybackStatus Resolve(PlaybackState? state)
        {
            if (state is null)
            {
                return PlaybackStatus.NotStarted;
            }

            // „Als gehört markieren" setzt nur IsCompleted und schreibt nie eine Position.
            // Würde die Position zuerst geprüft, zeigte die Detailseite solche Folgen
            // weiterhin als nicht begonnen — während die Statusleiste sie mitzählt,
            // weil sie direkt auf IsCompleted schaut.
            if (state.IsCompleted)
            {
                return PlaybackStatus.Finished;
            }

            return state.LastPosition == TimeSpan.Zero
                ? PlaybackStatus.NotStarted
                : PlaybackStatus.InProgress;
        }
    }
}
