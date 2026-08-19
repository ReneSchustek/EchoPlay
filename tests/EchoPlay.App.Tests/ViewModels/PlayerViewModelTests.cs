using EchoPlay.App.Tests.Fakes;
using System;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;

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

            PlaylistItemViewModel ersterEintrag = vm.PlaylistItems[0];
            playerService.SimulateExternalPlayback(tracks, currentPath: tracks[0]);

            Assert.Same(ersterEintrag, vm.PlaylistItems[0]);
        }

        [Fact]
        public async Task PlaylistItems_TracksAreTagged_ShowTitleFromTheFile()
        {
            // Auch die Wiedergabeliste des Players zeigt den Titel aus der Kennzeichnung;
            // ohne Titel bleibt der Dateiname ohne Endung und ohne Zeilennummer stehen.
            const string ersterTeil = @"C:\Audio\Serie\01 - Das leere Haus (Teil 1).mp3";
            const string zweiterTeil = @"C:\Audio\Serie\02 - Das leere Haus (Teil 2).mp3";

            FakeTrackTitleResolver titleResolver = new();
            titleResolver.TitlesByPath[ersterTeil] = "Das leere Haus (Teil 1)";

            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService, titleResolver);

            vm.LoadFiles([ersterTeil, zweiterTeil]);
            await vm.PendingTitleFill;

            Assert.Equal("Das leere Haus (Teil 1)", vm.PlaylistItems[0].Title);
            Assert.Equal("Das leere Haus (Teil 2)", vm.PlaylistItems[1].Title);
        }

        [Fact]
        public void CurrentTrackHighlight_FollowsThePath_NotTheDisplayedName()
        {
            // Der Anzeigename kommt aus der Kennzeichnung und passt dann nicht mehr zum
            // Dateinamen – hervorgehoben wird trotzdem die richtige Zeile.
            const string ersterTeil = @"C:\Audio\Serie\01 - Das leere Haus (Teil 1).mp3";
            const string zweiterTeil = @"C:\Audio\Serie\02 - Das leere Haus (Teil 2).mp3";

            FakeTrackTitleResolver titleResolver = new();
            titleResolver.TitlesByPath[zweiterTeil] = "Ein ganz anderer Titel";

            FakePlayerService playerService = new();
            PlayerViewModel vm = BuildViewModel(playerService, titleResolver);

            playerService.SimulateExternalPlayback([ersterTeil, zweiterTeil], currentPath: zweiterTeil);

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
            // ObservableCollection muss Einträge entfernbar sein – UI-Binding erfordert das
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
        public void PlayPause_SchaltetUmUndZeigtDasPassendeZeichen()
        {
            // Das Zeichen der Schaltfläche ist der einzige Hinweis darauf, was ein Klick
            // auslöst — zeigt es das falsche, drückt der Anwender auf Pause und startet.
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            Assert.Equal("\uE768", viewModel.PlayPauseGlyph);

            playerService.SetState("Track A", isPlaying: true, positionSeconds: 0, durationSeconds: 60);

            Assert.True(viewModel.IsPlaying);
            Assert.Equal("\uE769", viewModel.PlayPauseGlyph);
        }

        [Fact]
        public void Steuerbefehle_ErreichenDenAbspieler()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            viewModel.NextCommand.Execute(null);
            viewModel.PreviousCommand.Execute(null);

            Assert.Equal(1, playerService.SkipToNextCallCount);
            Assert.Equal(1, playerService.SkipToPreviousCallCount);
        }

        [Fact]
        public async Task DateiAuswahl_BautDieWiedergabeliste()
        {
            // Der Weg über die Dateiauswahl legt dieselbe Liste an wie das Öffnen eines
            // Ordners — nur ohne Zugriff auf das Dateisystem.
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            viewModel.LoadFiles([@"D:\audio\001.mp3", @"D:\audio\002.mp3"]);
            await viewModel.PendingTitleFill;

            Assert.Equal(2, viewModel.PlaylistItems.Count);
            Assert.Equal(@"D:\audio\001.mp3", viewModel.PlaylistItems[0].FullPath);
        }

        [Fact]
        public void DateiAuswahl_OhneListeWirftEineAussagekraeftigeAusnahme()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            _ = Assert.Throws<ArgumentNullException>(() => viewModel.LoadFiles(null!));
        }

        [Fact]
        public async Task LaufenderTitel_WirdInDerListeHervorgehoben()
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
        public void OhneCover_ZeigtDieSeiteDenPlatzhalter()
        {
            FakePlayerService playerService = new();
            PlayerViewModel viewModel = BuildViewModel(playerService);

            Assert.Null(viewModel.CoverImage);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.NoCoverVisibility);
        }

        [Fact]
        public async Task ZuletztGeoeffneterOrdner_WirdGemerktUndWiedergegeben()
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
