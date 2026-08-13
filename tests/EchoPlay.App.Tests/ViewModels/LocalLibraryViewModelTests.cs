using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LocalLibraryViewModel"/>.
    /// Prüft das Laden von Serien und Folgen in die Drei-Spalten-Ansicht
    /// sowie die SyncService-Integration beim Scan-Befehl.
    /// </summary>
    public sealed class LocalLibraryViewModelTests
    {
        /// <summary>
        /// Erzeugt ein vollständig verkabeltes ViewModel mit den übergebenen Fakes.
        /// <see cref="StatusBarViewModel"/> wird als Singleton innerhalb desselben
        /// ServiceProviders gebaut, damit keine separaten Scope-Probleme entstehen.
        /// </summary>
        private static LocalLibraryViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeLocalTrackDataService? trackService = null,
            FakeAppSettingsDataService? settingsService = null,
            FakeSyncService? syncService = null,
            FakePlayerService? playerService = null,
            FakeCoverSearchService? coverSearchService = null,
            FakeTrackTitleResolver? titleResolver = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => titleResolver ?? new FakeTrackTitleResolver());
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService ?? new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService ?? new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            FakeClock clock = new();

            StatusBarViewModel statusBar = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService(),
                clock);

            LocalLibraryViewModelContext context = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                syncService ?? new FakeSyncService(),
                playerService ?? new FakePlayerService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                statusBar,
                new FakeLocalCoverLoader(),
                new FakeScanEventService(),
                coverSearchService ?? new FakeCoverSearchService(),
                new FakeOnlineAccessGuard(),
                new FakeOnlineEpisodeChecker(),
                clock,
                MissingEpisodesCoordinator: new FakeMissingEpisodesCoordinator());

            return new LocalLibraryViewModel(context);
        }

        [Fact]
        public async Task LoadAsync_OnlyShowsSeriesWithLocalFolder()
        {
            // Nur Serien, für die der Scanner einen Ordner gefunden hat, erscheinen in der linken Spalte.
            // Serien ohne LocalFolderPath werden bewusst ausgeblendet – sie wurden noch nicht gescannt.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);
            await seriesService.AddAsync(new Series
            {
                Title = "Bibi Blocksberg",
                LocalFolderPath = null   // noch kein Ordner gefunden
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            // Nur TKKG hat einen lokalen Ordner – Bibi bleibt unsichtbar
            _ = Assert.Single(vm.ArtistsVM.Artists);
            Assert.Equal("TKKG", vm.ArtistsVM.Artists[0].Title);
        }

        [Fact]
        public async Task SelectArtistAsync_LoadsOnlyEpisodesWithLocalFolder()
        {
            // Nach Auswahl einer Serie werden nur Folgen mit LocalFolderPath in der mittleren Spalte gezeigt.
            // Folgen ohne Ordner sind noch nicht gescannt worden und sollen nicht erscheinen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);
            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 2",
                SeriesId = tkkg,
                EpisodeNumber = 2,
                LocalFolderPath = null   // noch nicht gescannt
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);

            // Nur Folge 1 hat einen lokalen Ordner
            _ = Assert.Single(vm.EpisodesVM.Episodes);
            Assert.Equal("001 \u2013 Folge 1", vm.EpisodesVM.Episodes[0].DisplayTitle);
        }

        [Fact]
        public async Task SelectEpisodeAsync_LoadsTracksInOrder()
        {
            // Tracks werden nach TrackNumber aufsteigend gelistet – wichtig für mehrteilige Folgen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid episodeId = episodeService.All[0].Id;

            Dictionary<Guid, IReadOnlyList<LocalTrack>> existingTracks = new()
            {
                [episodeId] =
                [
                    new LocalTrack { FilePath = @"C:\TKKG\001_b.mp3", TrackNumber = 2, Duration = TimeSpan.FromMinutes(10) },
                    new LocalTrack { FilePath = @"C:\TKKG\001_a.mp3", TrackNumber = 1, Duration = TimeSpan.FromMinutes(12) }
                ]
            };

            FakeLocalTrackDataService trackService = new(existingTracks);
            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService, trackService);
            await vm.Actions.LoadAsync();

            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);
            await vm.Actions.SelectEpisodeAsync(vm.EpisodesVM.Episodes[0]);

            Assert.Equal(2, vm.TracksVM.Tracks.Count);
            Assert.Equal(1, vm.TracksVM.Tracks[0].TrackNumber);
            Assert.Equal(2, vm.TracksVM.Tracks[1].TrackNumber);
        }

        [Fact]
        public async Task SelectEpisodeAsync_TracksAreTagged_ShowsTitleFromTheFile()
        {
            // Gepflegte Kennzeichnung: In der Liste steht der Titel, nicht der Dateiname.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "Sherlock Holmes",
                LocalFolderPath = @"C:\Hörspiele\Sherlock"
            }, cancellationToken: TestContext.Current.CancellationToken);

            await episodeService.AddAsync(new Episode
            {
                Title = "Das leere Haus",
                SeriesId = seriesService.All[0].Id,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\Sherlock\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);

            const string ersterTeil = @"C:\Hörspiele\Sherlock\001\01 - Das leere Haus (Teil 1).mp3";
            const string zweiterTeil = @"C:\Hörspiele\Sherlock\001\02 - Das leere Haus (Teil 2).mp3";

            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episodeService.All[0].Id] =
                [
                    new LocalTrack { FilePath = ersterTeil, TrackNumber = 1, Duration = TimeSpan.FromMinutes(4) },
                    new LocalTrack { FilePath = zweiterTeil, TrackNumber = 2, Duration = TimeSpan.FromMinutes(4) }
                ]
            });

            FakeTrackTitleResolver titleResolver = new();
            titleResolver.TitlesByPath[ersterTeil] = "Das leere Haus (Teil 1)";
            // Zweiter Teil ohne Titel in der Datei – dort greift die Rückfallebene.

            LocalLibraryViewModel vm = BuildViewModel(
                seriesService,
                episodeService,
                trackService,
                titleResolver: titleResolver);

            await vm.Actions.LoadAsync();
            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);
            await vm.Actions.SelectEpisodeAsync(vm.EpisodesVM.Episodes[0]);

            Assert.Equal("Das leere Haus (Teil 1)", vm.TracksVM.Tracks[0].Title);
            Assert.Equal("Das leere Haus (Teil 2)", vm.TracksVM.Tracks[1].Title);
        }

        [Fact]
        public async Task ScanCommand_TriggersSyncService()
        {
            // ScanCommand muss den SyncService aufrufen – ohne echten Scanner
            FakeSyncService syncService = new(result: new SyncResult
            {
                TracksCreated = 5,
                EpisodesUpdated = 2
            });

            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService, syncService: syncService);

            vm.ScanVM.ScanCommand.Execute(null);

            // Fire-and-forget: mit Task.FromResult-Fakes synchron ausgeführt
            Assert.Equal(1, syncService.SyncCallCount);
        }

        [Fact]
        public async Task LoadAsync_InitialState_BothAccordionsCollapsed()
        {
            // Nach LoadAsync ohne Auswahl müssen beide Accordion-Properties Collapsed sein
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            Assert.Equal(Visibility.Collapsed, vm.ArtistsVM.EpisodesAccordionVisibility);
            Assert.Equal(Visibility.Collapsed, vm.TracksVM.TracksAccordionVisibility);
            Assert.Equal(-1, vm.ArtistsVM.SelectedArtistIndex);
        }

        [Fact]
        public async Task SelectArtistAsync_EpisodesAccordion_BecomesVisible()
        {
            // Nach Auswahl einer Serie muss EpisodesAccordionVisibility Visible sein
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);

            Assert.Equal(Visibility.Visible, vm.ArtistsVM.EpisodesAccordionVisibility);
            Assert.Equal(0, vm.ArtistsVM.SelectedArtistIndex);
        }

        [Fact]
        public async Task SelectArtistAsync_SameSeriesTwice_DeselectsOnSecondCall()
        {
            // Re-Klick auf bereits ausgewählte Kachel klappt das Akkordeon wieder zu,
            // analog zum Schließen-Button und identisch zum Online-Mediathek-Verhalten.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 1
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);
            Assert.Equal(0, vm.ArtistsVM.SelectedArtistIndex);
            Assert.True(vm.ArtistsVM.Artists[0].IsSelectedInAccordion);

            // Re-Klick auf dieselbe Kachel → Toggle: Auswahl wird aufgehoben.
            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);

            Assert.Equal(-1, vm.ArtistsVM.SelectedArtistIndex);
            Assert.False(vm.ArtistsVM.Artists[0].IsSelectedInAccordion);
            Assert.Equal(Visibility.Collapsed, vm.ArtistsVM.EpisodesAccordionVisibility);
            Assert.Empty(vm.EpisodesVM.Episodes);
        }

        [Fact]
        public async Task SelectEpisodeAsync_TracksAccordion_BecomesVisible()
        {
            // Nach Auswahl einer Folge muss TracksAccordionVisibility Visible sein
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 1
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);
            await vm.Actions.SelectEpisodeAsync(vm.EpisodesVM.Episodes[0]);

            Assert.Equal(Visibility.Visible, vm.TracksVM.TracksAccordionVisibility);
        }

        // ── PlayEpisodeCommand ───────────────────────────────────────────────────

        [Fact]
        public async Task PlayEpisodeCommand_PassesTrackPathsInOrder()
        {
            // PlayEpisodeCommand muss alle Track-Pfade in aufsteigender TrackNumber-Reihenfolge
            // an den PlayerService übergeben – wichtig für die korrekte Wiedergabereihenfolge.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlayerService playerService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = tkkg,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid episodeId = episodeService.All[0].Id;

            Dictionary<Guid, IReadOnlyList<LocalTrack>> existingTracks = new()
            {
                [episodeId] =
                [
                    new LocalTrack { FilePath = @"C:\TKKG\001_b.mp3", TrackNumber = 2 },
                    new LocalTrack { FilePath = @"C:\TKKG\001_a.mp3", TrackNumber = 1 }
                ]
            };

            FakeLocalTrackDataService trackService = new(existingTracks);
            LocalLibraryViewModel vm = BuildViewModel(
                seriesService, episodeService, trackService, playerService: playerService);

            await vm.Actions.LoadAsync();
            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);
            await vm.Actions.SelectEpisodeAsync(vm.EpisodesVM.Episodes[0]);

            vm.TracksVM.PlayEpisodeCommand.Execute(null);
            await vm.TracksVM.PendingPlayback;

            _ = Assert.Single(playerService.PlayCalls);
            Assert.Equal(2, playerService.PlayCalls[0].TrackPaths.Count);
            // Track 1 (a.mp3) muss vor Track 2 (b.mp3) übergeben werden
            Assert.Equal(@"C:\TKKG\001_a.mp3", playerService.PlayCalls[0].TrackPaths[0]);
            Assert.Equal(@"C:\TKKG\001_b.mp3", playerService.PlayCalls[0].TrackPaths[1]);
        }

        [Fact]
        public async Task SelectEpisodeAsync_MakesTheTrackPanelVisible()
        {
            // Der Name der Pass-Through-Eigenschaft muss dem des Sub-VMs entsprechen: Die
            // Weiterleitung reicht den Namen unverändert durch. Wich er ab, blieb das Panel
            // unsichtbar, obwohl die Spuren geladen waren.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = seriesService.All[0].Id,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 1
            }, cancellationToken: TestContext.Current.CancellationToken);

            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episodeService.All[0].Id] =
                [
                    new LocalTrack { FilePath = @"C:\TKKG\001_a.mp3", TrackNumber = 1, Duration = TimeSpan.FromMinutes(12) }
                ]
            });

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService, trackService);
            await vm.Actions.LoadAsync();
            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);

            List<string> geaenderteEigenschaften = [];
            vm.TracksVM.PropertyChanged += (_, e) => geaenderteEigenschaften.Add(e.PropertyName ?? string.Empty);

            await vm.Actions.SelectEpisodeAsync(vm.EpisodesVM.Episodes[0]);

            Assert.Equal(Visibility.Visible, vm.TracksVM.TracksAccordionVisibility);
            Assert.Equal(Visibility.Visible, vm.TracksVM.TrackActionsVisibility);

            // Ohne diese Meldungen erfährt die Bindung nichts von der Änderung.
            Assert.Contains(nameof(LocalTracksViewModel.TracksAccordionVisibility), geaenderteEigenschaften);
            Assert.Contains(nameof(LocalTracksViewModel.TrackActionsVisibility), geaenderteEigenschaften);
        }

        [Fact]
        public async Task PlayEpisodeAsync_StartsTheEpisode_WithoutItsTracksBeingShown()
        {
            // Der Doppelklick startet über die Folgen-Id, nicht über die angezeigte Liste –
            // sonst liefe er ins Leere, solange die Spurenliste rechts noch lädt.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge 1",
                SeriesId = seriesService.All[0].Id,
                EpisodeNumber = 1,
                LocalFolderPath = @"C:\Hörspiele\TKKG\001",
                LocalTrackCount = 2
            }, cancellationToken: TestContext.Current.CancellationToken);

            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episodeService.All[0].Id] =
                [
                    new LocalTrack { FilePath = @"C:\TKKG\001_b.mp3", TrackNumber = 2, Duration = TimeSpan.FromMinutes(10) },
                    new LocalTrack { FilePath = @"C:\TKKG\001_a.mp3", TrackNumber = 1, Duration = TimeSpan.FromMinutes(12) }
                ]
            });

            FakePlayerService playerService = new();
            LocalLibraryViewModel vm = BuildViewModel(
                seriesService, episodeService, trackService, playerService: playerService);

            await vm.Actions.LoadAsync();
            await vm.Actions.SelectArtistAsync(vm.ArtistsVM.Artists[0]);

            // Bewusst ohne SelectEpisodeAsync: Es ist keine Folge ausgewählt, keine Liste geladen.
            await vm.TracksVM.PlayEpisodeAsync(vm.EpisodesVM.Episodes[0], TestContext.Current.CancellationToken);

            _ = Assert.Single(playerService.PlayCalls);
            Assert.Equal(@"C:\TKKG\001_a.mp3", playerService.PlayCalls[0].TrackPaths[0]);
            Assert.Equal(@"C:\TKKG\001_b.mp3", playerService.PlayCalls[0].TrackPaths[1]);
        }

        [Fact]
        public async Task PlayEpisodeCommand_IsDisabled_WhenNoTracksLoaded()
        {
            // PlayEpisodeCommand darf nicht ausführbar sein, solange keine Tracks geladen sind.
            // Verhindert leere Wiedergabe-Aufrufe an den PlayerService.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            // Noch keine Folge ausgewählt → keine Tracks → Befehl inaktiv
            Assert.False(vm.TracksVM.PlayEpisodeCommand.CanExecute(null));
        }

        // ── Fehlende Folgen ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowMissingEpisodesAsync_NonExistentFolder_ReportsNoFolder()
        {
            // Bei nicht-existierendem Ordner soll eine entsprechende Meldung kommen –
            // die Dateisystem-Analyse läuft nicht, weil der Ordner fehlt.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\NichtExistierenderPfad\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);

            // Serie muss erst geladen werden, damit die Karten vorhanden sind
            await vm.Actions.LoadAsync();

            IReadOnlyList<string>? received = null;
            vm.Actions.MissingEpisodesResolved += titles => received = titles;

            await vm.Actions.ShowMissingEpisodesAsync(tkkg);

            Assert.NotNull(received);
            _ = Assert.Single(received!);
            Assert.Contains("Kein lokaler Ordner", received![0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ShowMissingEpisodesAsync_NullFolder_ReportsNoFolder()
        {
            // Serie ohne LocalFolderPath → Meldung statt Absturz.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = null
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid tkkg = seriesService.All[0].Id;

            LocalLibraryViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.Actions.LoadAsync();

            IReadOnlyList<string>? received = null;
            vm.Actions.MissingEpisodesResolved += titles => received = titles;

            await vm.Actions.ShowMissingEpisodesAsync(tkkg);

            Assert.NotNull(received);
            _ = Assert.Single(received!);
            Assert.Contains("Kein lokaler Ordner", received![0], StringComparison.Ordinal);
        }

        // ── Cover-Verwaltung ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetCoverAsync_PersistsCoverBytesInCoverImagesTable()
        {
            // Cover-Persistenz läuft ausschließlich über die CoverImages-Tabelle;
            // Series.LocalCoverData existiert nicht mehr, der CoverImageDataService ist Single-Source-of-Truth.
            FakeSeriesDataService seriesService = new();
            FakeCoverImageDataService coverService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                LocalFolderPath = @"C:\Hörspiele\TKKG"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Guid seriesId = seriesService.All[0].Id;
            byte[] coverBytes = [0xFF, 0xD8, 0xFF];

            await coverService.SetCoverAsync(CoverEntityTypes.Series, seriesId, coverBytes, cancellationToken: TestContext.Current.CancellationToken);

            CoverImage? cover = await coverService.GetByEntityAsync(CoverEntityTypes.Series, seriesId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(cover);
            Assert.Equal(coverBytes, cover!.ImageData);
        }

    }
}
