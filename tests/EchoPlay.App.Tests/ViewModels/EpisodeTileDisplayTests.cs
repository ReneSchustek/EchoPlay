using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was eine Folgenkachel der Serienansicht anzeigt: Beschriftung, Dauer,
    /// Fortschrittsbalken, Haken und Platzhalter.
    /// </summary>
    /// <remarks>
    /// Die Kachel ist rein darstellend — genau deshalb fällt ein Fehler hier nirgends auf,
    /// außer beim Anwender: Eine Folge, die als gehört gilt, obwohl sie es nicht ist,
    /// verschwindet aus seinem Blick.
    /// </remarks>
    public sealed class EpisodeTileDisplayTests
    {
        [Fact]
        public void DisplayTitle_WithNumber_PutsItInFrontWithThreeDigits()
        {
            EpisodeTileViewModel sut = Build(episodeNumber: 7, title: "Der Karpatenhund");

            Assert.Equal("007 – Der Karpatenhund", sut.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_WithoutNumber_ShowsTheTitleAlone()
        {
            EpisodeTileViewModel sut = Build(episodeNumber: null, title: "Sonderfolge");

            Assert.Equal("Sonderfolge", sut.DisplayTitle);
        }

        [Fact]
        public void DurationText_WithDuration_ShowsHoursMinutesSeconds()
        {
            EpisodeTileViewModel sut = Build(totalDuration: new TimeSpan(1, 23, 45));

            Assert.Equal("1:23:45", sut.DurationText);
        }

        [Fact]
        public void DurationText_WithoutDuration_StaysEmpty()
        {
            EpisodeTileViewModel sut = Build(totalDuration: null);

            Assert.Equal(string.Empty, sut.DurationText);
        }

        [Fact]
        public void DurationText_WithZeroDuration_StaysEmpty()
        {
            // Eine noch nicht gelesene Dauer steht in der Ablage als Null. „0:00:00"
            // wäre eine Angabe, die es nicht gibt.
            EpisodeTileViewModel sut = Build(totalDuration: TimeSpan.Zero);

            Assert.Equal(string.Empty, sut.DurationText);
        }

        [Fact]
        public void ProgressBar_OnlyAppearsForStartedEpisodes()
        {
            Assert.Equal(Visibility.Visible, Build(status: PlaybackStatus.InProgress).ProgressBarVisibility);
            Assert.Equal(Visibility.Collapsed, Build(status: PlaybackStatus.NotStarted).ProgressBarVisibility);
            Assert.Equal(Visibility.Collapsed, Build(status: PlaybackStatus.Finished).ProgressBarVisibility);
        }

        [Fact]
        public void CompletedCheck_OnlyAppearsForFinishedEpisodes()
        {
            Assert.Equal(Visibility.Visible, Build(status: PlaybackStatus.Finished).CompletedCheckVisibility);
            Assert.Equal(Visibility.Collapsed, Build(status: PlaybackStatus.InProgress).CompletedCheckVisibility);
        }

        [Fact]
        public void NoCoverVisibility_WithoutCover_ShowsThePlaceholder()
        {
            EpisodeTileViewModel sut = Build();

            Assert.Equal(Visibility.Visible, sut.NoCoverVisibility);
        }

        [Fact]
        public void ActionsAutomationName_NamesTheEpisode()
        {
            EpisodeTileViewModel sut = Build(episodeNumber: 7, title: "Der Karpatenhund");

            // Die Vorlesehilfe liest sonst bei jeder Kachel denselben Text vor.
            Assert.Contains("007 – Der Karpatenhund", sut.ActionsAutomationName, StringComparison.Ordinal);
        }

        [Fact]
        public void PlayCommand_RunsTheHandedOverAction()
        {
            int started = 0;
            EpisodeTileViewModel sut = Build(playEpisode: () => started++);

            sut.PlayCommand.Execute(null);

            Assert.Equal(1, started);
        }

        private static EpisodeTileViewModel Build(
            int? episodeNumber = 1,
            string title = "Folge 1",
            TimeSpan? totalDuration = null,
            PlaybackStatus status = PlaybackStatus.NotStarted,
            Action? playEpisode = null)
            => new(
                TestIds.EpisodeA,
                episodeNumber,
                title,
                totalDuration,
                status,
                releaseDate: null,
                playEpisode ?? (() => { }),
                localizationService: new FakeLocalizationService(new Dictionary<string, string>
                {
                    ["TileActionsAutomationName"] = "Weitere Aktionen: {0}",
                }));
    }
}
