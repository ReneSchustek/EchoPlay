using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="MediathekLokalViewModel"/>.
    /// Prüft das Laden von Serien und Folgen in die Drei-Spalten-Ansicht
    /// sowie die SyncService-Integration beim Scan-Befehl.
    /// </summary>
    public sealed class MediathekLokalViewModelTests
    {
        /// <summary>
        /// Erzeugt ein vollständig verkabeltes ViewModel mit den übergebenen Fakes.
        /// <see cref="StatusBarViewModel"/> wird als Singleton innerhalb desselben
        /// ServiceProviders gebaut, damit keine separaten Scope-Probleme entstehen.
        /// </summary>
        private static MediathekLokalViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeLocalTrackDataService? trackService = null,
            FakeAppSettingsDataService? settingsService = null,
            FakeSyncService? syncService = null,
            FakePlayerService? playerService = null,
            FakeCoverSearchService? coverSearchService = null)
        {
            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => seriesService);
            services.AddScoped<IEpisodeDataService>(_ => episodeService);
            services.AddScoped<ILocalTrackDataService>(_ => trackService ?? new FakeLocalTrackDataService());
            services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            services.AddScoped<IAppSettingsDataService>(_ => settingsService ?? new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            StatusBarViewModel statusBar = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService());

            return new MediathekLokalViewModel(
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
                new FakeOnlineEpisodeChecker());
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
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });
            await seriesService.AddAsync(new Series
            {
                Title           = "Bibi Blocksberg",
                LocalFolderPath = null   // noch kein Ordner gefunden
            });

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title          = "Folge 1",
                SeriesId       = tkkg,
                LocalFolderPath = @"C:\Hoerspiele\TKKG\001",
                LocalTrackCount = 2
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            // Nur TKKG hat einen lokalen Ordner – Bibi bleibt unsichtbar
            Assert.Single(vm.Artists);
            Assert.Equal("TKKG", vm.Artists[0].Title);
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
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title           = "Folge 1",
                SeriesId        = tkkg,
                EpisodeNumber   = 1,
                LocalFolderPath = @"C:\Hoerspiele\TKKG\001",
                LocalTrackCount = 2
            });
            await episodeService.AddAsync(new Episode
            {
                Title           = "Folge 2",
                SeriesId        = tkkg,
                EpisodeNumber   = 2,
                LocalFolderPath = null   // noch nicht gescannt
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            await vm.SelectArtistAsync(vm.Artists[0]);

            // Nur Folge 1 hat einen lokalen Ordner
            Assert.Single(vm.Episodes);
            Assert.Equal("001 \u2013 Folge 1", vm.Episodes[0].DisplayTitle);
        }

        [Fact]
        public async Task SelectEpisodeAsync_LoadsTracksInOrder()
        {
            // Tracks werden nach TrackNumber aufsteigend gelistet – wichtig für mehrteilige Folgen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title           = "Folge 1",
                SeriesId        = tkkg,
                EpisodeNumber   = 1,
                LocalFolderPath = @"C:\Hoerspiele\TKKG\001",
                LocalTrackCount = 2
            });

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
            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService, trackService);
            await vm.LoadAsync();

            await vm.SelectArtistAsync(vm.Artists[0]);
            await vm.SelectEpisodeAsync(vm.Episodes[0]);

            Assert.Equal(2, vm.Tracks.Count);
            Assert.Equal(1, vm.Tracks[0].TrackNumber);
            Assert.Equal(2, vm.Tracks[1].TrackNumber);
        }

        [Fact]
        public async Task ScanCommand_TriggersSyncService()
        {
            // ScanCommand muss den SyncService aufrufen – ohne echten Scanner
            FakeSyncService syncService = new(result: new SyncResult
            {
                TracksCreated   = 5,
                EpisodesUpdated = 2
            });

            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService, syncService: syncService);

            vm.ScanCommand.Execute(null);

            // Fire-and-forget: mit Task.FromResult-Fakes synchron ausgeführt
            Assert.Equal(1, syncService.SyncCallCount);
        }

        [Fact]
        public async Task LoadAsync_InitialState_BothAccordionsCollapsed()
        {
            // Nach LoadAsync ohne Auswahl müssen beide Accordion-Properties Collapsed sein
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            Assert.Equal(Visibility.Collapsed, vm.EpisodesAccordionVisibility);
            Assert.Equal(Visibility.Collapsed, vm.TracksAccordionVisibility);
            Assert.Equal(-1, vm.SelectedArtistIndex);
        }

        [Fact]
        public async Task SelectArtistAsync_EpisodesAccordion_BecomesVisible()
        {
            // Nach Auswahl einer Serie muss EpisodesAccordionVisibility Visible sein
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            await vm.SelectArtistAsync(vm.Artists[0]);

            Assert.Equal(Visibility.Visible, vm.EpisodesAccordionVisibility);
            Assert.Equal(0, vm.SelectedArtistIndex);
        }

        [Fact]
        public async Task SelectEpisodeAsync_TracksAccordion_BecomesVisible()
        {
            // Nach Auswahl einer Folge muss TracksAccordionVisibility Visible sein
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title           = "Folge 1",
                SeriesId        = tkkg,
                EpisodeNumber   = 1,
                LocalFolderPath = @"C:\Hoerspiele\TKKG\001",
                LocalTrackCount = 1
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            await vm.SelectArtistAsync(vm.Artists[0]);
            await vm.SelectEpisodeAsync(vm.Episodes[0]);

            Assert.Equal(Visibility.Visible, vm.TracksAccordionVisibility);
        }

        // ── PlayEpisodeCommand ───────────────────────────────────────────────────

        [Fact]
        public async Task PlayEpisodeCommand_PassesTrackPathsInOrder()
        {
            // PlayEpisodeCommand muss alle Track-Pfade in aufsteigender TrackNumber-Reihenfolge
            // an den PlayerService übergeben – wichtig für die korrekte Wiedergabereihenfolge.
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();
            FakePlayerService playerService       = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            Guid tkkg = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title           = "Folge 1",
                SeriesId        = tkkg,
                EpisodeNumber   = 1,
                LocalFolderPath = @"C:\Hoerspiele\TKKG\001",
                LocalTrackCount = 2
            });

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
            MediathekLokalViewModel vm = BuildViewModel(
                seriesService, episodeService, trackService, playerService: playerService);

            await vm.LoadAsync();
            await vm.SelectArtistAsync(vm.Artists[0]);
            await vm.SelectEpisodeAsync(vm.Episodes[0]);

            vm.PlayEpisodeCommand.Execute(null);

            Assert.Single(playerService.PlayCalls);
            Assert.Equal(2, playerService.PlayCalls[0].TrackPaths.Count);
            // Track 1 (a.mp3) muss vor Track 2 (b.mp3) übergeben werden
            Assert.Equal(@"C:\TKKG\001_a.mp3", playerService.PlayCalls[0].TrackPaths[0]);
            Assert.Equal(@"C:\TKKG\001_b.mp3", playerService.PlayCalls[0].TrackPaths[1]);
        }

        [Fact]
        public async Task PlayEpisodeCommand_IsDisabled_WhenNoTracksLoaded()
        {
            // PlayEpisodeCommand darf nicht ausführbar sein, solange keine Tracks geladen sind.
            // Verhindert leere Wiedergabe-Aufrufe an den PlayerService.
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            // Noch keine Folge ausgewählt → keine Tracks → Befehl inaktiv
            Assert.False(vm.PlayEpisodeCommand.CanExecute(null));
        }

        // ── Fehlende Folgen ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowMissingEpisodesAsync_NonExistentFolder_ReportsNoFolder()
        {
            // Bei nicht-existierendem Ordner soll eine entsprechende Meldung kommen –
            // die Dateisystem-Analyse läuft nicht, weil der Ordner fehlt.
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\NichtExistierenderPfad\TKKG"
            });

            Guid tkkg = seriesService.All[0].Id;

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);

            // Serie muss erst geladen werden, damit die Karten vorhanden sind
            await vm.LoadAsync();

            IReadOnlyList<string>? received = null;
            vm.MissingEpisodesResolved += titles => received = titles;

            await vm.ShowMissingEpisodesAsync(tkkg);

            Assert.NotNull(received);
            Assert.Single(received!);
            Assert.Contains("Kein lokaler Ordner", received![0]);
        }

        [Fact]
        public async Task ShowMissingEpisodesAsync_NullFolder_ReportsNoFolder()
        {
            // Serie ohne LocalFolderPath → Meldung statt Absturz.
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = null
            });

            Guid tkkg = seriesService.All[0].Id;

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            IReadOnlyList<string>? received = null;
            vm.MissingEpisodesResolved += titles => received = titles;

            await vm.ShowMissingEpisodesAsync(tkkg);

            Assert.NotNull(received);
            Assert.Single(received!);
            Assert.Contains("Kein lokaler Ordner", received![0]);
        }

        // ── Cover-Verwaltung ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetLocalCoverAsync_PersistsCoverBytes()
        {
            // Prüft die DB-Ebene direkt: SetLocalCoverAsync muss die Bytes in der Serie speichern.
            // Der ViewModel-Weg über ApplySeriesCoverFromBytesAsync ist in Unit-Tests nicht testbar,
            // weil BitmapImageFromBytesAsync WinUI-3-COM-Typen benötigt (InMemoryRandomAccessStream),
            // die nur in der MSIX-Laufzeitumgebung verfügbar sind.
            FakeSeriesDataService seriesService = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            Guid seriesId = seriesService.All[0].Id;
            byte[] coverBytes = [0xFF, 0xD8, 0xFF];

            await seriesService.SetLocalCoverAsync(seriesId, coverBytes);

            Assert.Equal(coverBytes, seriesService.All[0].LocalCoverData);
        }

        [Fact]
        public async Task SearchAndApplySeriesCoverAsync_FiresEvent_WhenResultsFound()
        {
            // SearchAndApplySeriesCoverAsync muss SeriesCoverSearchResultsReady auslösen,
            // wenn der CoverSearchService Treffer zurückgibt.
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();
            FakeCoverSearchService coverSearch    = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                LocalFolderPath = @"C:\Hoerspiele\TKKG"
            });

            coverSearch.SetResults(
            [
                new CoverSearchResult("https://example.com/thumb.jpg", "https://example.com/full.jpg", "TKKG – Hörspiel", "Cover Art Archive")
            ]);

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService, coverSearchService: coverSearch);
            await vm.LoadAsync();

            IReadOnlyList<CoverSearchHit>? receivedResults = null;
            vm.SeriesCoverSearchResultsReady += (_, results) => receivedResults = results;

            await vm.SearchAndApplySeriesCoverAsync(vm.Artists[0]);

            Assert.NotNull(receivedResults);
            Assert.Single(receivedResults!);
            Assert.Equal("TKKG – Hörspiel", receivedResults![0].ReleaseTitle);
        }

        [Fact]
        public async Task SearchAndApplySeriesCoverAsync_DoesNotFireEvent_WhenNoResultsFound()
        {
            // Bei leerer Ergebnisliste darf SeriesCoverSearchResultsReady nicht ausgelöst werden.
            // Stattdessen zeigt das ViewModel einen Fehler-Dialog (FakeErrorDialogService schluckt ihn).
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();
            FakeCoverSearchService coverSearch    = new();

            await seriesService.AddAsync(new Series
            {
                Title           = "Unbekannte Serie",
                LocalFolderPath = @"C:\Hoerspiele\Unbekannt"
            });

            // Leere Ergebnisliste konfigurieren
            coverSearch.SetResults([]);

            MediathekLokalViewModel vm = BuildViewModel(seriesService, episodeService, coverSearchService: coverSearch);
            await vm.LoadAsync();

            bool eventFired = false;
            vm.SeriesCoverSearchResultsReady += (_, _) => eventFired = true;

            await vm.SearchAndApplySeriesCoverAsync(vm.Artists[0]);

            Assert.False(eventFired);
        }
    }
}
