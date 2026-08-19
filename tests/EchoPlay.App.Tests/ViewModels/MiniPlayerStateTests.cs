using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Anzeige des Mini-Abspielers: Zeitwerte, Fortschritt über die ganze Folge,
    /// Fehlermeldung und die Sprungbefehle.
    /// </summary>
    /// <remarks>
    /// Der Mini-Abspieler bekommt seine Werte über ein Ereignis des Abspieldienstes. Kommt
    /// eine Meldung nicht an, friert die Leiste bei den Werten der vorigen Folge ein —
    /// sichtbar falsch, aber ohne Fehlermeldung.
    ///
    /// Eigene Datei statt Ergänzung von <c>MiniPlayerViewModelTests</c>: Die liegt bereits
    /// nahe an der Grenze aus <c>testing.md</c>.
    /// </remarks>
    public sealed class MiniPlayerStateTests
    {
        [Fact]
        public void PositionAndDuration_FollowTheReportedState()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            player.SetState("Folge 1", isPlaying: true, positionSeconds: 90, durationSeconds: 600);

            Assert.Equal(90, sut.PositionSeconds);
            Assert.Equal(600, sut.DurationSeconds);
            Assert.True(sut.IsPlaying);
        }

        [Fact]
        public void ElapsedAndRemaining_ShowTheTimeOnBothSides()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            player.SetState("Folge 1", isPlaying: true, positionSeconds: 225, durationSeconds: 600);

            Assert.Equal("3:45", sut.ElapsedText);
            Assert.Equal("-6:15", sut.RemainingText);
        }

        [Fact]
        public void EpisodeProgress_WithASingleFile_StaysHidden()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            player.SetState("Folge 1", isPlaying: true, positionSeconds: 10, durationSeconds: 100);

            // Bei einer einzelnen Datei sagt der Folgenfortschritt nichts, was der
            // Zeitbalken nicht schon zeigt.
            Assert.Equal(Visibility.Collapsed, sut.EpisodeProgressVisibility);
        }

        [Fact]
        public void ErrorMessage_ArrivesFromThePlayer()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            player.SimulateError("Die Datei lässt sich nicht öffnen.");

            // Ohne diese Meldung bliebe die Leiste einfach stehen, und der Anwender
            // hielte die Anwendung für hängend.
            Assert.Equal("Die Datei lässt sich nicht öffnen.", sut.ErrorMessage);
        }

        [Fact]
        public void Volume_IsSharedWithThePlayer()
        {
            FakePlayerService player = new() { Volume = 0.4 };
            MiniPlayerViewModel sut = Build(player);

            Assert.NotNull(sut.Volume);
            Assert.Equal(40, sut.Volume.VolumePercent, precision: 3);
        }

        [Fact]
        public void PlaybackRate_IsHandedToThePlayer()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            sut.PlaybackRate = 1.5;

            Assert.Equal(1.5, player.PlaybackRate);
        }

        [Fact]
        public void NextAndPrevious_ReachThePlayer()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);

            sut.NextCommand.Execute(null);
            sut.PreviousCommand.Execute(null);

            Assert.Equal(1, player.SkipToNextCallCount);
            Assert.Equal(1, player.SkipToPreviousCallCount);
        }

        [Fact]
        public void PauseAndStop_ReachThePlayer()
        {
            FakePlayerService player = new();
            MiniPlayerViewModel sut = Build(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 0, durationSeconds: 100);

            sut.PauseCommand.Execute(null);
            sut.StopCommand.Execute(null);

            Assert.True(player.PauseWasCalled);
            Assert.True(player.StopWasCalled);
        }

        [Fact]
        public void TrackTitle_WhenTheSameFileKeepsPlaying_IsNotReadAgain()
        {
            FakePlayerService player = new();
            FakeTrackTitleResolver resolver = new();
            MiniPlayerViewModel sut = Build(player, resolver);

            player.SimulateExternalPlayback([TrackPath], TrackPath);
            player.SimulateExternalPlayback([TrackPath], TrackPath);

            // Jeder Tick des Abspielers löst die Meldung erneut aus. Würde der Titel jedes
            // Mal aus der Datei gelesen, liefe bei jeder Sekunde ein Dateizugriff.
            _ = Assert.Single(resolver.Requests);
            Assert.NotNull(sut.TrackTitle);
        }

        private const string TrackPath = @"D:\Media\TKKG\Folge 001\01 - Auftakt.mp3";

        private static MiniPlayerViewModel Build(
            FakePlayerService player, FakeTrackTitleResolver? resolver = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => resolver ?? new FakeTrackTitleResolver());
            ServiceProvider provider = services.BuildServiceProvider();

            return new MiniPlayerViewModel(player, provider.GetRequiredService<IServiceScopeFactory>());
        }
    }
}
