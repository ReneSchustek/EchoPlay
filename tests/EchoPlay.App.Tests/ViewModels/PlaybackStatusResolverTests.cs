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
