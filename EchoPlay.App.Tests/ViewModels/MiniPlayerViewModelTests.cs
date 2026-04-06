using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="MiniPlayerViewModel"/>.
    /// Prüft die Reaktion auf StateChanged-Events und die Weitergabe von Befehlen.
    /// </summary>
    public sealed class MiniPlayerViewModelTests
    {
        [Fact]
        public void TrackTitle_UpdatesFromStateChanged()
        {
            // StateChanged muss TrackTitle aktualisieren
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("TKKG – Folge 1", isPlaying: true, positionSeconds: 0, durationSeconds: 60);

            Assert.Equal("TKKG – Folge 1", vm.TrackTitle);
        }

        [Fact]
        public void IsPlaying_UpdatesFromStateChanged()
        {
            // IsPlaying muss den aktuellen Wiedergabestatus widerspiegeln
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("Folge 1", isPlaying: true, positionSeconds: 0, durationSeconds: 60);
            Assert.True(vm.IsPlaying);

            playerService.SetState("Folge 1", isPlaying: false, positionSeconds: 0, durationSeconds: 60);
            Assert.False(vm.IsPlaying);
        }

        [Fact]
        public void PositionSeconds_UpdatesFromStateChanged()
        {
            // PositionSeconds muss der übergebenen Position entsprechen
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("Folge 1", isPlaying: true, positionSeconds: 42.5, durationSeconds: 3600);

            Assert.Equal(42.5, vm.PositionSeconds);
        }

        [Fact]
        public void SeekTo_CallsPlayerService()
        {
            // SeekTo muss die Position korrekt an den PlayerService weiterreichen
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.SeekTo(120.0);

            Assert.Equal(TimeSpan.FromSeconds(120.0), playerService.SeekToArg);
        }

        [Fact]
        public void PlayCommand_CallsResume()
        {
            // PlayCommand entspricht Resume – nicht Play (kein neuer Track)
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.PlayCommand.Execute(null);

            Assert.True(playerService.ResumeWasCalled);
        }

        [Fact]
        public void PauseCommand_CallsPause()
        {
            // PauseCommand muss Pause auf dem PlayerService aufrufen
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.PauseCommand.Execute(null);

            Assert.True(playerService.PauseWasCalled);
        }

        [Fact]
        public void PlaybackRate_Setter_PropagatesTo_PlayerService()
        {
            // Geschwindigkeitsänderung im ViewModel muss den PlayerService aktualisieren
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.PlaybackRate = 1.5;

            Assert.Equal(1.5, playerService.PlaybackRate);
        }

        [Fact]
        public void PlaybackRate_SameValue_DoesNotPropagate()
        {
            // SetProperty gibt false zurück wenn der Wert gleich bleibt – kein Seiteneffekt
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.PlaybackRate = 1.0; // Startwert ist bereits 1.0

            Assert.Equal(1.0, playerService.PlaybackRate);
        }

        [Fact]
        public void SetSleepTimer_DelegatesToPlayerService()
        {
            // SetSleepTimer im ViewModel muss den PlayerService aufrufen
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            vm.SetSleepTimer(TimeSpan.FromMinutes(30));

            Assert.True(playerService.SetSleepTimerWasCalled);
            Assert.Equal(TimeSpan.FromMinutes(30), playerService.LastSetSleepTimerArg);
        }

        [Fact]
        public void SleepTimerText_ShowsFormattedCountdown_WhenTimerActive()
        {
            // StateChanged nach SetSleepTimer muss SleepTimerText als mm:ss formatieren
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetSleepTimer(TimeSpan.FromMinutes(30));

            Assert.Equal("30:00", vm.SleepTimerText);
        }

        [Fact]
        public void SleepTimerText_IsEmpty_WhenNoTimerActive()
        {
            // Ohne aktiven Timer bleibt SleepTimerText leer
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("Folge 1", isPlaying: true, positionSeconds: 0, durationSeconds: 60);

            Assert.Equal(string.Empty, vm.SleepTimerText);
        }

        [Fact]
        public void SleepTimerText_IsEmpty_AfterTimerDeactivated()
        {
            // Deaktivieren des Timers (null) muss SleepTimerText leeren
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetSleepTimer(TimeSpan.FromMinutes(15));
            playerService.SetSleepTimer(null);

            Assert.Equal(string.Empty, vm.SleepTimerText);
        }

        // ── Brief 100: MiniPlayer-Sichtbarkeit bei lokaler Wiedergabe ─────────────

        [Fact]
        public void MiniPlayerVisibility_IsCollapsed_Initially()
        {
            // Ohne laufende Wiedergabe soll der MiniPlayer nicht sichtbar sein.
            // Verhindert, dass der MiniPlayer beim App-Start leer angezeigt wird.
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            Assert.Equal(Visibility.Collapsed, vm.MiniPlayerVisibility);
        }

        [Fact]
        public void MiniPlayerVisibility_BecomesVisible_WhenPlaybackStarts()
        {
            // Sobald StateChanged mit einem Track-Titel feuert, muss der MiniPlayer sichtbar werden.
            // Wird ausgelöst wenn IPlayerService.Play() aus der lokalen Mediathek aufgerufen wird.
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("TKKG 001 – Die Schwarze Hand", isPlaying: true, positionSeconds: 0, durationSeconds: 2400);

            Assert.Equal(Visibility.Visible, vm.MiniPlayerVisibility);
        }

        [Fact]
        public void MiniPlayerVisibility_CollapsesAgain_WhenPlaybackStops()
        {
            // Nach dem Stopp (TrackTitle wird auf leer gesetzt) muss der MiniPlayer wieder verschwinden.
            FakePlayerService playerService = new();
            MiniPlayerViewModel vm = new(playerService);

            playerService.SetState("TKKG 001", isPlaying: true, positionSeconds: 0, durationSeconds: 2400);
            Assert.Equal(Visibility.Visible, vm.MiniPlayerVisibility);

            // Stopp: CurrentTrackTitle null → UpdateFromState setzt TrackTitle auf leer
            playerService.SetState(trackTitle: null, isPlaying: false, positionSeconds: 0, durationSeconds: 0);

            Assert.Equal(Visibility.Collapsed, vm.MiniPlayerVisibility);
        }
    }
}
