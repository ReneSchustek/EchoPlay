using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Lebenslauf der lokalen Mediathek: Betreten und Verlassen der Seite,
    /// das Aufheben der Serienauswahl und den Weg in den Tag-Manager.
    /// </summary>
    /// <remarks>
    /// Der wichtigste Punkt ist das Abmelden: Der Einlesedienst lebt als Singleton weiter.
    /// Bleibt die Seite an seinem Ereignis hängen, tragen sich Kacheln in eine Ansicht ein,
    /// die niemand mehr sieht — und die Seite bleibt im Speicher.
    /// </remarks>
    public sealed class LocalLibraryViewModelLifecycleTests
    {
        [Fact]
        public async Task InitializeAsync_WithoutPageGuard_LetsThePageLoad()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);

            Assert.True(await sut.InitializeAsync());
        }

        [Fact]
        public async Task InitializeAsync_WhenTheGuardDeniesLocalAccess_StopsThePage()
        {
            FakePageModeGuard guard = new(allow: false);
            LocalLibraryViewModel sut = BuildViewModel(out _, guard);

            // Im Nur-Online-Betrieb gibt es keine lokale Mediathek; die Seite darf sich
            // gar nicht erst aufbauen, sondern navigiert zurück.
            Assert.False(await sut.InitializeAsync());
            Assert.Equal(1, guard.CallCount);
        }

        [Fact]
        public async Task InitializeAsync_WhenTheGuardAllows_LetsThePageLoad()
        {
            FakePageModeGuard guard = new(allow: true);
            LocalLibraryViewModel sut = BuildViewModel(out _, guard);

            Assert.True(await sut.InitializeAsync());
        }

        [Fact]
        public void DeselectArtist_ClearsEpisodesAndTracksAsWell()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);

            sut.DeselectArtist();

            // Der ganze Folgenbereich verschwindet — bliebe eine der drei Listen stehen,
            // zeigte die Seite Folgen zu einer Serie, die gar nicht mehr gewählt ist.
            Assert.Null(sut.ArtistsVM.SelectedArtist);
            Assert.Empty(sut.EpisodesVM.Episodes);
            Assert.Empty(sut.TracksVM.Tracks);
        }

        [Fact]
        public void RequestTagManagerNavigation_PassesTheFolderToTheSubscriber()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);
            string? requested = null;
            sut.NavigateToTagManagerRequested += path => requested = path;

            sut.RequestTagManagerNavigation(FolderPath);

            Assert.Equal(FolderPath, requested);
        }

        [Fact]
        public void RequestTagManagerNavigation_WithoutSubscriber_StaysSilent()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);

            sut.RequestTagManagerNavigation(FolderPath);
        }

        [Fact]
        public void Deactivate_AfterActivate_KeepsLateScanResultsOutOfTheView()
        {
            LocalLibraryViewModel sut = BuildViewModel(out FakeScanEventService scanEvents);
            sut.Activate();

            sut.Deactivate();
            scanEvents.RaiseSeriesSynced(new Series { Title = "TKKG", LocalFolderPath = FolderPath });

            Assert.Empty(sut.ArtistsVM.Artists);
        }

        [Fact]
        public void Dispose_AfterActivate_KeepsLateScanResultsOutOfTheView()
        {
            LocalLibraryViewModel sut = BuildViewModel(out FakeScanEventService scanEvents);
            sut.Activate();

            sut.Dispose();
            scanEvents.RaiseSeriesSynced(new Series { Title = "TKKG", LocalFolderPath = FolderPath });

            Assert.Empty(sut.ArtistsVM.Artists);
        }

        [Fact]
        public void ResetFiltersCommand_ClearsSearchTextAndFilters()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);
            sut.ArtistsVM.LocalSearchText = "TKKG";
            sut.ArtistsVM.FavoritesOnly = true;

            sut.ResetFiltersCommand.Execute(null);

            // Vom Hinweis „nichts gefunden" führt genau ein Schritt zurück zum Bestand.
            Assert.Equal(string.Empty, sut.ArtistsVM.LocalSearchText);
            Assert.False(sut.ArtistsVM.FavoritesOnly);
        }

        [Fact]
        public void CheckAllSeriesCommand_IsAvailableOnThePage()
        {
            LocalLibraryViewModel sut = BuildViewModel(out _);

            Assert.NotNull(sut.CheckAllSeriesCommand);
            Assert.True(sut.CheckAllSeriesCommand.CanExecute(null));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private const string FolderPath = @"D:\Media\TKKG";

        private static LocalLibraryViewModel BuildViewModel(
            out FakeScanEventService scanEvents,
            FakePageModeGuard? pageModeGuard = null)
        {
            scanEvents = new FakeScanEventService();

            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            IClock clock = new FakeClock();

            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), clock);

            LocalLibraryViewModelContext context = new(
                scopeFactory,
                new FakeSyncService(),
                new FakePlayerService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                statusBar,
                new FakeLocalCoverLoader(),
                scanEvents,
                new FakeCoverSearchService(),
                new FakeOnlineAccessGuard(),
                new FakeOnlineEpisodeChecker(),
                clock,
                PageModeGuard: pageModeGuard,
                MissingEpisodesCoordinator: new FakeMissingEpisodesCoordinator());

            return new LocalLibraryViewModel(context);
        }
    }
}
