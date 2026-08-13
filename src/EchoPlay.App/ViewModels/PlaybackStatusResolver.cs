using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Playback;
using System;

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

        /// <summary>
        /// Gibt an, ob die Folge an einer offenen Stelle steht — ob es also etwas
        /// weiterzuhören gibt.
        /// </summary>
        /// <remarks>
        /// Das ist bewusst eine eigene Aussage neben <see cref="Resolve"/>: Ein Hörspiel
        /// hört man nicht einmal im Leben. Wer eine bereits gehörte Folge erneut startet
        /// und in der Mitte aufhört, hat sie weiterhin gehört — und trotzdem eine Stelle,
        /// an der er fortsetzen möchte. Beides in einen Wert zu pressen hieß bisher, dass
        /// „gehört" gewinnt und die Folge nirgends mehr auftaucht, wo man sie wiederfindet.
        ///
        /// Die Gesamtdauer gehört zum Vergleich, weil eine durchgehörte Folge ihre Position
        /// am Ende stehen hat — ohne sie zählte jede fertige Folge als angefangen. Rund die
        /// Hälfte des Bestands führt allerdings gar keine Dauer; dort entscheidet die
        /// Position allein. Das ist gemessen unbedenklich: Im gesamten Bestand tragen nur
        /// drei gehörte Folgen überhaupt eine stehengebliebene Position.
        /// </remarks>
        /// <param name="state">Der gespeicherte Wiedergabezustand oder <c>null</c>.</param>
        /// <param name="episodeDuration">Die Gesamtdauer der Folge; <see cref="TimeSpan.Zero"/>, wenn unbekannt.</param>
        /// <returns><see langword="true"/>, wenn eine offene Stelle vorliegt.</returns>
        public static bool HasOpenPosition(PlaybackState? state, TimeSpan episodeDuration)
        {
            return state is not null
                   && EchoPlay.App.Services.EpisodeTimeline.HasOpenPosition(state.LastPosition, episodeDuration);
        }
    }
}
