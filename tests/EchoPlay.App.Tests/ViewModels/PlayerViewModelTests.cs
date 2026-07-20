using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="PlayerViewModel"/>.
    /// Prüft Playlist-Verwaltung und Reaktion auf PlayerService-Ereignisse.
    /// </summary>
    public sealed class PlayerViewModelTests
    {
        private static PlayerViewModel BuildViewModel(FakePlayerService playerService)
        {
            // ScopeFactory nur für SaveLastOpenedFolderAsync benötigt – hier nicht relevant
            ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

            return new PlayerViewModel(playerService, provider.GetRequiredService<IServiceScopeFactory>());
        }

        [Fact]
        public void PlaylistItems_ReflectPlayerServiceState()
        {
            // StateChanged-Event des PlayerService muss ViewModel-Properties aktualisieren
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            playerService.SetState("Track A", isPlaying: true, positionSeconds: 10.0, durationSeconds: 120.0);

            Assert.True(vm.IsPlaying);
            Assert.Equal("Track A", vm.CurrentTitle);
            Assert.Equal(120.0, vm.DurationSeconds);
        }

        [Fact]
        public void PlayPauseCommand_TogglesIsPlaying()
        {
            // Play/Pause-Command muss den richtigen Service-Aufruf auslösen
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            // Ausgangszustand: nicht spielend → Resume erwartet
            vm.PlayPauseCommand.Execute(null);
            Assert.True(playerService.ResumeWasCalled);

            // Zustand auf "spielt" setzen → nächster Klick pausiert
            playerService.SetState("Track A", isPlaying: true, positionSeconds: 0, durationSeconds: 60);
            vm.PlayPauseCommand.Execute(null);
            Assert.True(playerService.PauseWasCalled);
        }

        [Fact]
        public void LoadFiles_AddsAllFilesToPlaylist()
        {
            // Alle übergebenen Dateipfade müssen als PlaylistItems erscheinen
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            List<string> paths = ["file1.mp3", "file2.mp3", "file3.mp3"];
            vm.LoadFiles(paths);

            Assert.Equal(3, vm.PlaylistItems.Count);
            // PlayerService muss einmal mit allen Pfaden aufgerufen worden sein
            _ = Assert.Single(playerService.PlayCalls);
        }

        [Fact]
        public void RemoveItem_RemovesFromPlaylist()
        {
            // ObservableCollection muss Einträge entfernbar sein – UI-Binding erfordert das
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            vm.LoadFiles(["a.mp3", "b.mp3", "c.mp3"]);
            Assert.Equal(3, vm.PlaylistItems.Count);

            vm.PlaylistItems.RemoveAt(1);

            Assert.Equal(2, vm.PlaylistItems.Count);
        }
    }
}
