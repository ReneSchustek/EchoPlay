using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="PlayerViewModel"/>.
    /// Prüft Playlist-Verwaltung und Reaktion auf PlayerService-Ereignisse.
    /// </summary>
    public sealed class PlayerViewModelTests
    {
        [Fact]
        public void StopObservingPlayer_LaterPlayerChanges_NoLongerReachTheViewModel()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);
            playerService.SetState("Erste Spur", isPlaying: true, positionSeconds: 10, durationSeconds: 60);
            Assert.True(vm.IsPlaying);

            vm.StopObservingPlayer();
            playerService.SetState("Zweite Spur", isPlaying: false, positionSeconds: 0, durationSeconds: 60);

            // Die verlassene Seite bleibt auf ihrem letzten Stand; der Dienst ruft sie nicht mehr.
            Assert.True(vm.IsPlaying);
        }

        private static PlayerViewModel BuildViewModel(FakePlayerService playerService)
        {
            return BuildViewModel(playerService, new FakeTrackTitleResolver());
        }

        private static PlayerViewModel BuildViewModel(FakePlayerService playerService, FakeTrackTitleResolver titleResolver)
        {
            // ScopeFactory liefert den Titel-Auflöser für die Wiedergabeliste
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => titleResolver);
            ServiceProvider provider = services.BuildServiceProvider();

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
            Assert.Equal(120.0, vm.Time.DurationSeconds);
        }

        [Fact]
        public void PlaylistItems_PlaybackStartedElsewhere_AdoptsRunningList()
        {
            // Der übliche Weg: Die Wiedergabe startet in der Mediathek oder der Serienansicht,
            // die Player-Seite war daran nicht beteiligt. Sie muss die laufende Liste trotzdem
            // zeigen - sonst steht dort der Platzhalter, während unten die Folge läuft.
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            Assert.Empty(vm.PlaylistItems);

            playerService.SimulateExternalPlayback(
                [@"C:\Audio\Serie\01 - Erster Teil.mp3", @"C:\Audio\Serie\02 - Zweiter Teil.mp3"],
                currentPath: @"C:\Audio\Serie\02 - Zweiter Teil.mp3");

            Assert.Equal(2, vm.PlaylistItems.Count);
            Assert.Equal("2 Tracks", vm.PlaylistSubtitle);
            Assert.Equal("Zweiter Teil", vm.CurrentTitle);

            // Die laufende Spur ist hervorgehoben, nicht die erste der Liste.
            Assert.False(vm.PlaylistItems[0].IsCurrentTrack);
            Assert.True(vm.PlaylistItems[1].IsCurrentTrack);

            // Anzeigen, nicht neu starten: Ein erneutes Play begänne bei der ersten Spur und
            // ohne Folgenbezug, der Hörstand der laufenden Folge würde nicht mehr gespeichert.
            Assert.Empty(playerService.PlayCalls);
        }

        [Fact]
        public void PlaylistItems_SameListAgain_IsNotRebuilt()
        {
            // StateChanged feuert im Sekundentakt aus dem Positions-Timer. Ohne Abgleich
            // entstünde bei jedem Tick eine neue Liste - die Auswahl in der Ansicht wäre weg.
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            List<string> tracks = [@"C:\Audio\Serie\01 - Erster Teil.mp3"];
            playerService.SimulateExternalPlayback(tracks, currentPath: tracks[0]);

            PlaylistItemViewModel firstEntry = vm.PlaylistItems[0];
            playerService.SimulateExternalPlayback(tracks, currentPath: tracks[0]);

            Assert.Same(firstEntry, vm.PlaylistItems[0]);
        }

        [Fact]
        public async Task PlaylistItems_TracksAreTagged_ShowTitleFromTheFile()
        {
            // Auch die Wiedergabeliste des Players zeigt den Titel aus der Kennzeichnung;
            // ohne Titel bleibt der Dateiname ohne Endung und ohne Zeilennummer stehen.
            const string firstPart = @"C:\Audio\Serie\01 - Das leere Haus (Teil 1).mp3";
            const string secondPart = @"C:\Audio\Serie\02 - Das leere Haus (Teil 2).mp3";

            FakeTrackTitleResolver titleResolver = new();
            titleResolver.TitlesByPath[firstPart] = "Das leere Haus (Teil 1)";

            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService, titleResolver);

            vm.LoadFiles([firstPart, secondPart]);
            await vm.PendingTitleFill;

            Assert.Equal("Das leere Haus (Teil 1)", vm.PlaylistItems[0].Title);
            Assert.Equal("Das leere Haus (Teil 2)", vm.PlaylistItems[1].Title);
        }

        [Fact]
        public void CurrentTrackHighlight_FollowsThePath_NotTheDisplayedName()
        {
            // Der Anzeigename kommt aus der Kennzeichnung und passt dann nicht mehr zum
            // Dateinamen. Hervorgehoben wird trotzdem die richtige Zeile.
            const string firstPart = @"C:\Audio\Serie\01 - Das leere Haus (Teil 1).mp3";
            const string secondPart = @"C:\Audio\Serie\02 - Das leere Haus (Teil 2).mp3";

            FakeTrackTitleResolver titleResolver = new();
            titleResolver.TitlesByPath[secondPart] = "Ein ganz anderer Titel";

            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService, titleResolver);

            playerService.SimulateExternalPlayback([firstPart, secondPart], currentPath: secondPart);

            Assert.False(vm.PlaylistItems[0].IsCurrentTrack);
            Assert.True(vm.PlaylistItems[1].IsCurrentTrack);
            Assert.Equal("Ein ganz anderer Titel", vm.CurrentTitle);
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
            // ObservableCollection muss Einträge entfernbar sein, weil UI-Binding das erfordert
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            vm.LoadFiles(["a.mp3", "b.mp3", "c.mp3"]);
            Assert.Equal(3, vm.PlaylistItems.Count);

            vm.PlaylistItems.RemoveAt(1);

            Assert.Equal(2, vm.PlaylistItems.Count);
        }

        [Fact]
        public void LoadFiles_StartsPlaybackImmediately()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            vm.LoadFiles([@"D:\Media\01.mp3", @"D:\Media\02.mp3"]);

            // Einen Ordner zu öffnen heißt hören zu wollen: Die Wiedergabe beginnt sofort
            // beim ersten Titel, ohne dass noch einmal geklickt werden muss.
            _ = Assert.Single(playerService.PlayCalls);
            Assert.Equal(0, playerService.PlayCalls[0].Item3);
        }

        [Fact]
        public void LoadFiles_WithEmptySelection_StartsNothing()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            vm.LoadFiles([]);

            // Eine leere Auswahl darf das Abspielgerät nicht mit einer leeren Liste anwerfen.
            Assert.Empty(playerService.PlayCalls);
            Assert.Empty(vm.PlaylistItems);
        }

        [Fact]
        public void PlayItem_StartsAtTheClickedEntry()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);
            vm.LoadFiles([@"D:\Media\01.mp3", @"D:\Media\02.mp3", @"D:\Media\03.mp3"]);

            vm.PlayItem(vm.PlaylistItems[2]);

            // Wer auf den dritten Titel klickt, will den dritten hören. Beginnt die
            // Wiedergabe stattdessen wieder vorn, ist die Liste als Bedienelement wertlos.
            Assert.Equal(2, playerService.PlayCalls.Count);
            Assert.Equal(2, playerService.PlayCalls[^1].Item3);
        }

        [Fact]
        public void PlayItem_PassesTheWholeListNotOnlyTheClickedFile()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);
            vm.LoadFiles([@"D:\Media\01.mp3", @"D:\Media\02.mp3"]);

            vm.PlayItem(vm.PlaylistItems[0]);

            // Die ganze Liste geht mit, damit nach dem ersten Titel der nächste folgt.
            // Nur die angeklickte Datei zu übergeben, beendete die Wiedergabe nach einem Stück.
            Assert.Equal(2, playerService.PlayCalls[^1].Item2.Count);
        }

        [Fact]
        public void PlayItem_WithoutItem_StartsNothingNew()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);
            vm.LoadFiles([@"D:\Media\01.mp3"]);
            int afterLoading = playerService.PlayCalls.Count;

            vm.PlayItem(null);

            Assert.Equal(afterLoading, playerService.PlayCalls.Count);
        }

        [Fact]
        public void LoadFiles_WithoutPathList_Throws()
        {
            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService);

            _ = Assert.Throws<ArgumentNullException>(() => vm.LoadFiles(null!));
        }
        [Fact]
        public void PlayPause_TogglesAndShowsTheMatchingGlyph()
        {
            // Das Zeichen der Schaltfläche ist der einzige Hinweis darauf, was ein Klick
            // auslöst. Zeigt es das falsche, drückt der Anwender auf Pause und startet.
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            Assert.Equal("\uE768", viewModel.PlayPauseGlyph);

            playerService.SetState("Track A", isPlaying: true, positionSeconds: 0, durationSeconds: 60);

            Assert.True(viewModel.IsPlaying);
            Assert.Equal("\uE769", viewModel.PlayPauseGlyph);
        }

        [Fact]
        public void StopCommand_WhileRunning_StopsThePlayerService()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);
            playerService.SetState("Track A", isPlaying: true, positionSeconds: 42, durationSeconds: 60);

            viewModel.StopCommand.Execute(null);

            Assert.True(playerService.StopWasCalled);
        }

        [Fact]
        public void StopCommand_AfterStop_DisplayIsIdle()
        {
            // Nach dem Stopp darf nichts mehr so aussehen, als liefe eine Spur: kein Titel,
            // keine hervorgehobene Zeile, die Zeitanzeige auf null.
            const string firstPart = @"C:\Audio\Serie\01 - Teil 1.mp3";
            const string secondPart = @"C:\Audio\Serie\02 - Teil 2.mp3";
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);
            playerService.SimulateExternalPlayback([firstPart, secondPart], currentPath: secondPart);

            viewModel.StopCommand.Execute(null);

            Assert.Equal(string.Empty, viewModel.CurrentTitle);
            Assert.DoesNotContain(viewModel.PlaylistItems, item => item.IsCurrentTrack);
            Assert.False(viewModel.IsPlaying);
            Assert.Equal(0d, viewModel.Time.PositionSeconds);
        }

        [Fact]
        public void StopCommand_AfterStop_KeepsThePlaylistForReplay()
        {
            const string firstPart = @"C:\Audio\Serie\01 - Teil 1.mp3";
            const string secondPart = @"C:\Audio\Serie\02 - Teil 2.mp3";
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);
            playerService.SimulateExternalPlayback([firstPart, secondPart], currentPath: firstPart);

            viewModel.StopCommand.Execute(null);

            Assert.Equal(2, viewModel.PlaylistItems.Count);
        }

        [Fact]
        public void ControlCommands_ReachThePlayer()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            viewModel.NextCommand.Execute(null);
            viewModel.PreviousCommand.Execute(null);

            Assert.Equal(1, playerService.SkipToNextCallCount);
            Assert.Equal(1, playerService.SkipToPreviousCallCount);
        }

        [Fact]
        public async Task FileSelection_BuildsThePlaylist()
        {
            // Der Weg über die Dateiauswahl legt dieselbe Liste an wie das Öffnen eines
            // Ordners, nur ohne Zugriff auf das Dateisystem.
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            viewModel.LoadFiles([@"D:\audio\001.mp3", @"D:\audio\002.mp3"]);
            await viewModel.PendingTitleFill;

            Assert.Equal(2, viewModel.PlaylistItems.Count);
            Assert.Equal(@"D:\audio\001.mp3", viewModel.PlaylistItems[0].FullPath);
        }

        [Fact]
        public void FileSelection_WithoutList_ThrowsAMeaningfulException()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            _ = Assert.Throws<ArgumentNullException>(() => viewModel.LoadFiles(null!));
        }

        [Fact]
        public async Task RunningTitle_IsHighlightedInTheList()
        {
            // Verglichen wird der Dateipfad: Der Anzeigename kann aus der Kennzeichnung
            // stammen und passt dann nicht mehr zum Dateinamen.
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);
            viewModel.LoadFiles([@"D:\audio\001.mp3", @"D:\audio\002.mp3"]);
            await viewModel.PendingTitleFill;

            playerService.SimulateExternalPlayback([@"D:\audio\001.mp3", @"D:\audio\002.mp3"], @"D:\audio\002.mp3");

            Assert.False(viewModel.PlaylistItems[0].IsCurrentTrack);
            Assert.True(viewModel.PlaylistItems[1].IsCurrentTrack);
        }

        [Fact]
        public void WithoutCover_ThePageShowsThePlaceholder()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            Assert.Null(viewModel.CoverImage);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.NoCoverVisibility);
        }

        [Fact]
        public async Task LastOpenedFolder_IsRememberedAndReturned()
        {
            FakePlayerService playerService = new();
            FakeAppSettingsDataService settings = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();

            PlayerViewModel viewModel = new(playerService, provider.GetRequiredService<IServiceScopeFactory>());

            Assert.Null(await viewModel.GetLastOpenedFolderAsync());

            await viewModel.SaveLastOpenedFolderAsync(@"D:\audio");

            Assert.Equal(@"D:\audio", await viewModel.GetLastOpenedFolderAsync());
            Assert.Equal(1, settings.SaveCallCount);
        }
    }
}
