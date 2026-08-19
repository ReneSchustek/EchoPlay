using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Aktionen der lokalen Mediathek: Überwachung, „alles gehört" und die beiden
    /// Wege, eine Serie loszuwerden.
    /// </summary>
    /// <remarks>
    /// Zwei dieser Vorgänge löschen — einer nur den Eintrag, einer die Dateien. Beide fragen
    /// vorher nach, und beide müssen die Ablehnung ernst nehmen. Ein Test darauf ist billiger
    /// als der Anruf eines Anwenders, dessen Hörspielsammlung verschwunden ist.
    /// <para>
    /// Eigene Datei statt Ergänzung von <c>LocalLibraryViewModelTests</c>: Die Datei liegt
    /// bereits über der Grenze aus <c>testing.md</c>, und diese Klasse ist ohnehin eine eigene.
    /// </para>
    /// </remarks>
    public sealed class LocalLibraryActionsTests
    {
        [Fact]
        public async Task ToggleWatch_WithoutService_DoesNothing()
        {
            Harness harness = Harness.Build(watchToggleService: null);

            await harness.Actions.ToggleWatchAsync(Guid.NewGuid(), watch: true);

            // Ohne Dienst gibt es keine Überwachung. Ein Aufruf ins Leere dürfte die
            // Anwendung nicht mitreißen.
            Assert.Empty(harness.SeriesService.All);
        }

        [Fact]
        public async Task ToggleWatch_WithService_PassesTheNewState()
        {
            FakeWatchToggleService watchToggle = new();
            Harness harness = Harness.Build(watchToggleService: watchToggle);
            Guid seriesId = Guid.NewGuid();

            await harness.Actions.ToggleWatchAsync(seriesId, watch: true);

            Assert.Equal([(seriesId, true)], watchToggle.Calls);
        }

        [Fact]
        public async Task MarkAllAsRead_MarksEveryEpisodeOfTheSeries()
        {
            Harness harness = Harness.Build();

            Series series = new() { Title = "Die drei Fragezeichen", LocalFolderPath = @"D:\Media" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);

            Episode first = new() { SeriesId = series.Id, Title = "Folge 1" };
            Episode second = new() { SeriesId = series.Id, Title = "Folge 2" };
            await harness.EpisodeService.AddAsync(first, TestContext.Current.CancellationToken);
            await harness.EpisodeService.AddAsync(second, TestContext.Current.CancellationToken);

            await harness.Actions.MarkAllAsReadAsync(series.Id);

            // „Alles gehört" muss jede Folge treffen. Bliebe eine offen, stünde die Serie
            // weiter unter den angefangenen — genau das wollte der Anwender beenden.
            System.Collections.Generic.IReadOnlyList<PlaybackState> states =
                await harness.PlaybackService.GetAllAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, states.Count);
            Assert.All(states, state => Assert.True(state.IsCompleted));
        }

        [Fact]
        public async Task DeleteFromLibrary_WhenUserDeclines_KeepsTheSeries()
        {
            Harness harness = Harness.Build(confirmResult: false);

            Series series = new() { Title = "Bleibt", LocalFolderPath = @"D:\Media" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);

            await harness.Actions.DeleteSeriesFromLibraryAsync(series.Id);

            _ = Assert.Single(harness.SeriesService.All);
        }

        [Fact]
        public async Task DeleteFromLibrary_WhenConfirmed_RemovesTheEntry()
        {
            Harness harness = Harness.Build(confirmResult: true);

            Series series = new() { Title = "Verschwindet", LocalFolderPath = @"D:\Media" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);

            await harness.Actions.DeleteSeriesFromLibraryAsync(series.Id);

            // Nur der Eintrag geht — die Dateien bleiben liegen und werden beim nächsten
            // Einlesen wiedergefunden.
            Assert.Empty(harness.SeriesService.All);
        }

        [Fact]
        public async Task DeleteFromDisk_WhenUserDeclines_TouchesNothing()
        {
            Harness harness = Harness.Build(confirmResult: false);

            Series series = new() { Title = "Bleibt auf der Platte", LocalFolderPath = @"D:\Media\Serie" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);

            await harness.Actions.DeleteSeriesFromDiskAsync(series.Id, series.LocalFolderPath);

            // Dieser Weg löscht unwiderruflich. Wer im Dialog ablehnt, muss alles behalten.
            _ = Assert.Single(harness.SeriesService.All);
        }

        [Fact]
        public async Task ShowMissingEpisodes_WithoutCoordinator_ReportsNothing()
        {
            Harness harness = Harness.Build();
            bool reported = false;
            harness.Actions.MissingEpisodesResolved += _ => reported = true;

            await harness.Actions.ShowMissingEpisodesAsync(Guid.NewGuid());

            // Fehlt der Koordinator, gibt es keinen Bericht. Ein leeres Ergebnis zu melden
            // hieße „nichts fehlt" — das wäre eine Aussage, die niemand geprüft hat.
            Assert.False(reported);
        }

        [Fact]
        public async Task CheckAllSeries_WithoutCoordinator_ReportsNothing()
        {
            Harness harness = Harness.Build();
            bool reported = false;
            harness.Actions.AllSeriesCheckCompleted += _ => reported = true;

            await harness.Actions.CheckAllSeriesAsync();

            Assert.False(reported);
        }

        [Fact]
        public async Task ExecuteRestructure_WithoutCoordinator_MovesNothing()
        {
            Harness harness = Harness.Build();

            int moved = await harness.Actions.ExecuteRestructureAsync(
                new RestructurePreviewDisplay(
                    new EchoPlay.LocalLibrary.Models.RestructurePreview
                    {
                        SeriesFolderPath = @"D:\Media\Serie",
                        Actions = [],
                    }));

            // Null verschobene Dateien ist hier die ehrliche Antwort: Ohne Assistent wurde
            // nichts angefasst, und die Anzeige darf keinen Erfolg melden.
            Assert.Equal(0, moved);
        }

        [Fact]
        public async Task SearchCovers_WithoutCoordinator_ReturnsNoHits()
        {
            Harness harness = Harness.Build();

            System.Collections.Generic.IReadOnlyList<CoverSearchHit> hits =
                await harness.Actions.SearchCoversAsync(
                    "Die drei Fragezeichen", CoverSearchPage.First, TestContext.Current.CancellationToken);

            Assert.Empty(hits);
        }

        [Fact]
        public async Task FehlendeFolgen_FragtOhneDialogNurDenBestand()
        {
            // Ohne angemeldeten Dialog gilt "nur offline" - ein Unit-Test darf keine
            // Anbieter-Abfrage auslösen.
            FakeMissingEpisodesCoordinator coordinator = new();
            Harness harness = Harness.Build(missingEpisodesCoordinator: coordinator);

            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);
            await harness.ArtistsVM.LoadFromDatabaseAsync();

            System.Collections.Generic.IReadOnlyList<string>? reported = null;
            harness.Actions.MissingEpisodesResolved += result => reported = result;

            await harness.Actions.ShowMissingEpisodesAsync(series.Id);

            (Guid SeriesId, string? FolderPath, MissingEpisodesMode Mode) call = Assert.Single(coordinator.SingleCalls);
            Assert.Equal(series.Id, call.SeriesId);
            Assert.Equal(@"D:\Media\TKKG", call.FolderPath);
            Assert.Equal(MissingEpisodesMode.OfflineOnly, call.Mode);
            Assert.NotNull(reported);
        }

        [Fact]
        public async Task FehlendeFolgen_AbbruchImDialogPrueftNichts()
        {
            FakeMissingEpisodesCoordinator coordinator = new();
            Harness harness = Harness.Build(missingEpisodesCoordinator: coordinator);
            harness.Actions.MissingEpisodesModeRequested += () => Task.FromResult(MissingEpisodesMode.Cancel);

            bool reported = false;
            harness.Actions.MissingEpisodesResolved += _ => reported = true;

            await harness.Actions.ShowMissingEpisodesAsync(Guid.NewGuid());

            Assert.Empty(coordinator.SingleCalls);
            Assert.False(reported);
        }

        [Fact]
        public async Task GesamtpruefungMeldetDenBericht()
        {
            FakeMissingEpisodesCoordinator coordinator = new();
            Harness harness = Harness.Build(missingEpisodesCoordinator: coordinator);

            EchoPlay.Core.Models.MissingEpisodesReport? report = null;
            harness.Actions.AllSeriesCheckCompleted += result => report = result;

            await harness.Actions.CheckAllSeriesAsync();

            Assert.Equal([MissingEpisodesMode.OfflineOnly], coordinator.AllCalls);
            Assert.NotNull(report);
        }

        [Fact]
        public async Task Gesamtprüfung_AbbruchImDialogMeldetNichts()
        {
            FakeMissingEpisodesCoordinator coordinator = new();
            Harness harness = Harness.Build(missingEpisodesCoordinator: coordinator);
            harness.Actions.MissingEpisodesModeRequested += () => Task.FromResult(MissingEpisodesMode.Cancel);

            bool reported = false;
            harness.Actions.AllSeriesCheckCompleted += _ => reported = true;

            await harness.Actions.CheckAllSeriesAsync();

            Assert.Empty(coordinator.AllCalls);
            Assert.False(reported);
        }

        [Fact]
        public async Task Ordnerassistent_OhneVorschauMeldetNichts()
        {
            // Kein verschiebbarer Bestand heißt: kein Dialog. Ein leeres Fenster wäre
            // schlimmer als gar keins.
            FakeFolderRestructureCoordinator coordinator = new() { PreviewToReturn = null };
            Harness harness = Harness.Build(restructureCoordinator: coordinator);

            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);
            await harness.ArtistsVM.LoadFromDatabaseAsync();

            bool announced = false;
            harness.Actions.RestructurePreviewReady += _ => announced = true;

            await harness.Actions.AnalyzeRestructureAsync(series.Id);

            Assert.Equal([@"D:\Media\TKKG"], coordinator.AnalyzedFolders);
            Assert.False(announced);
        }

        [Fact]
        public async Task Ordnerassistent_MeldetDieVorschau()
        {
            RestructurePreviewDisplay preview = new(
                new EchoPlay.LocalLibrary.Models.RestructurePreview
                {
                    SeriesFolderPath = @"D:\Media\TKKG",
                    Actions =
                    [
                        new EchoPlay.LocalLibrary.Models.RestructureAction
                        {
                            SourcePath = @"D:\Media\TKKG\01.mp3",
                            TargetFolderPath = @"D:\Media\TKKG\01 - Der Superhund",
                            TargetFolderName = "001 - Der Superhund",
                            FileName = "001.mp3"
                        }
                    ]
                });

            FakeFolderRestructureCoordinator coordinator = new() { PreviewToReturn = preview };
            Harness harness = Harness.Build(restructureCoordinator: coordinator);

            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);
            await harness.ArtistsVM.LoadFromDatabaseAsync();

            RestructurePreviewDisplay? announced = null;
            harness.Actions.RestructurePreviewReady += result => announced = result;

            await harness.Actions.AnalyzeRestructureAsync(series.Id);

            Assert.Same(preview, announced);
        }

        [Fact]
        public async Task Ordnerassistent_OhneOrdnerAmEintragAnalysiertNichts()
        {
            FakeFolderRestructureCoordinator coordinator = new();
            Harness harness = Harness.Build(restructureCoordinator: coordinator);

            await harness.Actions.AnalyzeRestructureAsync(Guid.NewGuid());

            Assert.Empty(coordinator.AnalyzedFolders);
        }

        [Fact]
        public async Task Ordnerassistent_MeldetDieAnzahlVerschobenerDateien()
        {
            FakeFolderRestructureCoordinator coordinator = new() { MovedFileCount = 12 };
            Harness harness = Harness.Build(restructureCoordinator: coordinator);

            RestructurePreviewDisplay preview = new(
                new EchoPlay.LocalLibrary.Models.RestructurePreview
                {
                    SeriesFolderPath = @"D:\Media\TKKG",
                    Actions = []
                });

            int moved = await harness.Actions.ExecuteRestructureAsync(preview);

            Assert.Equal(12, moved);
            Assert.Equal(1, coordinator.ExecuteCallCount);
        }

        [Fact]
        public async Task Coversuche_ReichtBegriffUndAbschnittWeiter()
        {
            FakeEpisodeCoverCoordinator coordinator = new()
            {
                Hits = [new CoverSearchHit("thumb", "voll", "TKKG 1", "Test")]
            };
            Harness harness = Harness.Build(coverCoordinator: coordinator);

            System.Collections.Generic.IReadOnlyList<CoverSearchHit> hits =
                await harness.Actions.SearchCoversAsync("TKKG", CoverSearchPage.First.Next, TestContext.Current.CancellationToken);

            Assert.Equal(("TKKG", CoverSearchPage.First.Next), Assert.Single(coordinator.SearchCalls));
            _ = Assert.Single(hits);
        }

        [Fact]
        public async Task Coveruebernahme_LaeuftUeberDenKoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new();
            Harness harness = Harness.Build(coverCoordinator: coordinator);

            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);
            await harness.ArtistsVM.LoadFromDatabaseAsync();
            LocalArtistCardViewModel card = harness.ArtistsVM.AllArtists[0];
            CoverSearchHit hit = new("thumb", "voll", "TKKG 1", "Test");

            await harness.Actions.ApplySeriesCoverFromBytesAsync(card, [1, 2, 3]);
            await harness.Actions.ApplySelectedSeriesCoverAsync(card, hit);

            Assert.Equal([1, 2, 3], Assert.Single(coordinator.SeriesCoverBytes).Bytes);
            Assert.Same(hit, Assert.Single(coordinator.SelectedSeriesCovers).Hit);
        }

        [Fact]
        public async Task Coverübernahme_OhneKoordinatorBleibtWirkungslos()
        {
            // Ohne Koordinator darf der Aufruf nichts tun und nichts reißen - die Kachel
            // behält ihr bisheriges Bild.
            Harness harness = Harness.Build();

            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await harness.SeriesService.AddAsync(series, TestContext.Current.CancellationToken);
            await harness.ArtistsVM.LoadFromDatabaseAsync();
            LocalArtistCardViewModel card = harness.ArtistsVM.AllArtists[0];

            await harness.Actions.ApplySeriesCoverFromBytesAsync(card, [1]);
            await harness.Actions.ApplySelectedSeriesCoverAsync(card, new CoverSearchHit("t", "v", "r", "s"));

            Assert.Null(card.CoverImage);
        }

        /// <summary>
        /// Baut die Aktionen samt Kontext. Zusammengefasst in einem Typ, weil die Tests auf
        /// mehrere der beteiligten Fakes zugreifen müssen.
        /// </summary>
        private sealed class Harness
        {
            public required LocalLibraryActions Actions { get; init; }

            public required FakeSeriesDataService SeriesService { get; init; }

            public required FakeEpisodeDataService EpisodeService { get; init; }

            public required FakePlaybackStateDataService PlaybackService { get; init; }

            public required LocalArtistsViewModel ArtistsVM { get; init; }

            public static Harness Build(
                bool confirmResult = true,
                FakeWatchToggleService? watchToggleService = null,
                FakeMissingEpisodesCoordinator? missingEpisodesCoordinator = null,
                FakeFolderRestructureCoordinator? restructureCoordinator = null,
                FakeEpisodeCoverCoordinator? coverCoordinator = null)
            {
                FakeSeriesDataService seriesService = new();
                FakeEpisodeDataService episodeService = new();
                FakePlaybackStateDataService playbackService = new();

                ServiceCollection services = new();
                _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
                _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
                _ = services.AddScoped<IPlaybackStateDataService>(_ => playbackService);
                _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
                _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
                _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

                ServiceProvider provider = services.BuildServiceProvider();
                IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                StatusBarViewModel statusBar = new(
                    scopeFactory, new FakeThemeService(), new TaskbarProgressService(), new FakeClock());

                LocalLibraryScanViewModel scanVM = new(
                    scopeFactory,
                    new FakeSyncService(),
                    new FakeErrorDialogService(),
                    new FakeConfirmationDialogService(confirmResult),
                    statusBar,
                    new FakeScanEventService(),
                    _ => { });

                LocalArtistsViewModel artistsVM = new(scopeFactory);
                LocalEpisodesViewModel episodesVM = new(scopeFactory, new FakeLocalCoverLoader(), new FakeClock());
                LocalTracksViewModel tracksVM = new(new FakePlayerService(), scopeFactory, _ => { });

                LocalLibraryActions actions = new(
                    scopeFactory,
                    new FakeConfirmationDialogService(confirmResult),
                    statusBar,
                    new FakeCoverSearchService(),
                    new FakeOnlineAccessGuard(),
                    watchToggleService,
                    restructureCoordinator,
                    missingEpisodesCoordinator,
                    coverCoordinator,
                    new FakeClock(),
                    scanVM,
                    artistsVM,
                    episodesVM,
                    tracksVM);

                return new Harness
                {
                    Actions = actions,
                    SeriesService = seriesService,
                    EpisodeService = episodeService,
                    PlaybackService = playbackService,
                    ArtistsVM = artistsVM,
                };
            }
        }
    }
}
