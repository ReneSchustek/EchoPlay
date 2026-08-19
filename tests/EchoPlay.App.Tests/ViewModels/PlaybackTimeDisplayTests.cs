using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Zeitanzeige des Abspielers: verstrichene Zeit, Restzeit, Fortschritt über
    /// die ganze Folge und den Suchlauf am Regler.
    /// </summary>
    /// <remarks>
    /// Der Suchlauf trägt die wichtigste Zusage: Solange der Anwender den Regler hält,
    /// darf der Dienst die Position nicht überschreiben — sonst springt der Regler unter
    /// dem Finger zurück.
    /// </remarks>
    public sealed class PlaybackTimeDisplayTests
    {
        [Fact]
        public void Zeitanzeige_ZeigtVerstricheneUndVerbleibendeZeit()
        {
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 225, durationSeconds: 3600);

            display.Refresh();

            Assert.Equal("3:45", display.ElapsedText);
            Assert.Equal("-56:15", display.RemainingOrTotalText);
            Assert.Equal(225, display.PositionSeconds);
            Assert.Equal(3600, display.DurationSeconds);
        }

        [Fact]
        public void Umschalten_ZeigtStattDerRestzeitDieGesamtdauer()
        {
            // Beide Angaben teilen sich denselben Platz. Der Wechsel ist die einzige
            // Möglichkeit, die Gesamtlänge einer Folge überhaupt zu sehen.
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 60, durationSeconds: 3600);
            display.Refresh();

            display.ToggleTimeDisplayCommand.Execute(null);

            Assert.Equal("1:00:00", display.RemainingOrTotalText);

            display.ToggleTimeDisplayCommand.Execute(null);

            Assert.Equal("-59:00", display.RemainingOrTotalText);
        }

        [Fact]
        public void Suchlauf_HaeltDenReglerBeimAnwender()
        {
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 10, durationSeconds: 3600);
            display.Refresh();

            display.BeginSeek();
            display.PositionSeconds = 900;

            // Der Dienst läuft weiter — der Regler bleibt trotzdem, wo der Anwender ihn hält.
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 20, durationSeconds: 3600);
            display.Refresh();

            Assert.Equal(900, display.PositionSeconds);

            display.CommitSeek();

            Assert.Equal(TimeSpan.FromSeconds(900), player.SeekToArg);
        }

        [Fact]
        public void OhneSuchlauf_WirdDerReglerNichtVerstellt()
        {
            // Der Setter gehört dem Suchlauf. Ohne ihn kommt die Position nur vom Dienst.
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 10, durationSeconds: 3600);
            display.Refresh();

            display.PositionSeconds = 900;

            Assert.Equal(10, display.PositionSeconds);
        }

        [Fact]
        public void Folgenfortschritt_ErscheintNurBeiMehrerenDateien()
        {
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);

            Assert.Equal(Visibility.Collapsed, display.EpisodeProgressVisibility);

            player.SimulateExternalPlayback([@"D:\audio\001.mp3", @"D:\audio\002.mp3"], @"D:\audio\001.mp3");
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 60, durationSeconds: 600);
            display.Refresh();

            Assert.Equal(Visibility.Visible, display.EpisodeProgressVisibility);
            Assert.Equal(10, display.EpisodeProgressPercent);
            Assert.Contains("von", display.EpisodeProgressText, StringComparison.Ordinal);
        }

        [Fact]
        public void Zurücksetzen_LeertReglerUndTexte()
        {
            FakePlayerService player = new();
            PlaybackTimeDisplay display = new(player);
            player.SetState("Folge 1", isPlaying: true, positionSeconds: 225, durationSeconds: 3600);
            display.Refresh();

            player.Stop();
            display.Reset();

            Assert.Equal(0, display.PositionSeconds);
            Assert.Equal(0, display.DurationSeconds);
            Assert.Equal("0:00", display.ElapsedText);
        }
    }
}
