using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Playback;
using System;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="PlaybackStatusResolver"/> – die eine Stelle, an der aus dem
    /// gespeicherten Wiedergabezustand der angezeigte Status wird. Sie entscheidet über
    /// das Symbol auf jeder Kachel und über den Zähler „X von Y Folgen gehört".
    /// </summary>
    public sealed class PlaybackStatusResolverTests
    {
        [Fact]
        public void Resolve_WithoutState_IsNotStarted()
        {
            Assert.Equal(PlaybackStatus.NotStarted, PlaybackStatusResolver.Resolve(null));
        }

        [Fact]
        public void Resolve_FreshState_IsNotStarted()
        {
            PlaybackState state = new() { EpisodeId = Helpers.TestIds.EpisodeA };

            Assert.Equal(PlaybackStatus.NotStarted, PlaybackStatusResolver.Resolve(state));
        }

        [Fact]
        public void Resolve_WithPosition_IsInProgress()
        {
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                LastPosition = TimeSpan.FromMinutes(5)
            };

            Assert.Equal(PlaybackStatus.InProgress, PlaybackStatusResolver.Resolve(state));
        }

        [Fact]
        public void Resolve_CompletedWithPosition_IsFinished()
        {
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                LastPosition = TimeSpan.FromMinutes(41),
                IsCompleted = true
            };

            Assert.Equal(PlaybackStatus.Finished, PlaybackStatusResolver.Resolve(state));
        }

        [Fact]
        public void Resolve_MarkedAsPlayedWithoutPosition_IsFinished()
        {
            // „Als gehört markieren" setzt nur IsCompleted — die Position bleibt null.
            // Vor der Korrektur galt eine so markierte Folge weiter als nicht begonnen.
            PlaybackState state = new() { EpisodeId = Helpers.TestIds.EpisodeA };
            state.MarkCompleted(Helpers.TestIds.ReferenceDate);

            Assert.Equal(TimeSpan.Zero, state.LastPosition);
            Assert.Equal(PlaybackStatus.Finished, PlaybackStatusResolver.Resolve(state));
        }

        [Fact]
        public void HasOpenPosition_WithoutState_IsFalse()
        {
            Assert.False(PlaybackStatusResolver.HasOpenPosition(null, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HasOpenPosition_MarkedByHand_IsFalse()
        {
            // Wer von Hand „gehört" sagt, hat nichts Offenes mehr. Ohne diese Regel stünde
            // eine so gekennzeichnete Folge mit alter Position weiter unter „Angefangen".
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                LastPosition = TimeSpan.FromMinutes(20)
            };

            state.MarkCompleted(Helpers.TestIds.ReferenceDate);

            Assert.False(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HasOpenPosition_FullyHeard_IsFalse()
        {
            // Durchgehört heißt: Position steht am Ende.
            PlaybackState state = new() { EpisodeId = Helpers.TestIds.EpisodeA };
            state.UpdatePosition(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            Assert.False(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HasOpenPosition_HeardAndStartedAgain_IsTrue()
        {
            // Der Kern des Vorgangs: Ein Hörspiel hört man nicht einmal im Leben. Die Folge
            // bleibt gehört und hat trotzdem eine Stelle, an der weiterzuhören ist.
            PlaybackState state = new() { EpisodeId = Helpers.TestIds.EpisodeA };
            state.UpdatePosition(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            state.UpdatePosition(TimeSpan.FromMinutes(12), TimeSpan.FromHours(1));

            Assert.True(state.IsCompleted);
            Assert.Equal(PlaybackStatus.Finished, PlaybackStatusResolver.Resolve(state));
            Assert.True(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HasOpenPosition_SecondPassFinished_IsFalseAgain()
        {
            // Nach dem zweiten Durchhören ist wieder nichts offen — sonst bliebe die Folge
            // dauerhaft unter „Angefangen" stehen.
            PlaybackState state = new() { EpisodeId = Helpers.TestIds.EpisodeA };
            state.UpdatePosition(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            state.UpdatePosition(TimeSpan.FromMinutes(12), TimeSpan.FromHours(1));

            state.UpdatePosition(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            Assert.Equal(TimeSpan.FromHours(1), state.LastPosition);
            Assert.False(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HasOpenPosition_UnknownDuration_PositionDecidesAlone()
        {
            // Rund die Hälfte des Bestands führt keine Dauer. Dort gibt es nichts, wogegen
            // sich die Position prüfen ließe — also gilt sie. Der Hörstatus darf hier nicht
            // entscheiden, sonst bliebe genau der Fall unsichtbar, um den es geht.
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                LastPosition = TimeSpan.FromMinutes(5),
                IsCompleted = true
            };

            Assert.True(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.Zero));
        }

        [Fact]
        public void HasOpenPosition_UnknownDuration_WithoutPosition_IsFalse()
        {
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                IsCompleted = true
            };

            Assert.False(PlaybackStatusResolver.HasOpenPosition(state, TimeSpan.Zero));
        }

        [Fact]
        public void Resolve_AfterReset_IsNotStarted()
        {
            PlaybackState state = new()
            {
                EpisodeId = Helpers.TestIds.EpisodeA,
                LastPosition = TimeSpan.FromMinutes(12),
                IsCompleted = true
            };

            state.Reset();

            Assert.Equal(PlaybackStatus.NotStarted, PlaybackStatusResolver.Resolve(state));
        }
    }
}
