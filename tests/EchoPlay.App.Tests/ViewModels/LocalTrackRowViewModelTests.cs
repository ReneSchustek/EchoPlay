using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using System;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Zeile einer einzelnen Spur in der lokalen Mediathek: die Zeitangabe und
    /// den Sprung in den Tag-Manager.
    /// </summary>
    public sealed class LocalTrackRowViewModelTests
    {
        private const string TrackPath = @"D:\Media\TKKG\Folge 001\01 - Auftakt.mp3";

        [Fact]
        public void DurationText_UnderAnHour_ShowsMinutesAndSeconds()
        {
            LocalTrackRowViewModel sut = Build(TimeSpan.FromSeconds(754));

            Assert.Equal("12:34", sut.DurationText);
        }

        [Fact]
        public void DurationText_FromAnHourOn_ShowsHoursAsWell()
        {
            LocalTrackRowViewModel sut = Build(new TimeSpan(1, 23, 45));

            // Ohne die Stunde läse sich eine 83-Minuten-Datei als 23 Minuten.
            Assert.Equal("1:23:45", sut.DurationText);
        }

        [Fact]
        public void DurationText_WithoutDuration_ShowsZero()
        {
            LocalTrackRowViewModel sut = Build(TimeSpan.Zero);

            Assert.Equal("0:00", sut.DurationText);
        }

        [Fact]
        public void OpenInTagManager_HandsOverTheFolderNotTheFile()
        {
            List<string> requested = [];
            LocalTrackRowViewModel sut = Build(TimeSpan.Zero, requested.Add);

            sut.OpenInTagManagerCommand.Execute(null);

            // Der Tag-Manager arbeitet auf dem Ordner: So sieht der Anwender gleich alle
            // Dateien der Folge und nicht nur die eine, die er angeklickt hat.
            Assert.Equal([@"D:\Media\TKKG\Folge 001"], requested);
        }

        [Fact]
        public void OpenInTagManager_WithoutFolderInThePath_StaysQuiet()
        {
            List<string> requested = [];
            LocalTrackRowViewModel sut = new(
                TestIds.TrackA, trackNumber: 1, filePath: "nur-ein-dateiname.mp3",
                TimeSpan.Zero, requested.Add);

            sut.OpenInTagManagerCommand.Execute(null);

            Assert.Empty(requested);
        }

        [Fact]
        public void Title_BeforeReadingTheTag_ComesFromTheFileName()
        {
            LocalTrackRowViewModel sut = Build(TimeSpan.Zero);

            // Bis die Kennzeichnung gelesen ist, steht der aufgeräumte Dateiname da —
            // die vorangestellte Nummer gehört nicht in die Beschriftung.
            Assert.Equal("Auftakt", sut.Title);
        }

        private static LocalTrackRowViewModel Build(
            TimeSpan duration, Action<string>? onNavigate = null)
            => new(
                TestIds.TrackA,
                trackNumber: 1,
                TrackPath,
                duration,
                onNavigate ?? (_ => { }));
    }
}
