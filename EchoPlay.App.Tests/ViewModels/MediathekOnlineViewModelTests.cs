using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="MediathekOnlineViewModel"/>.
    /// Prüft das Laden der Serienliste und das korrekte Mapping der Felder.
    /// Alle Test-Serien erhalten <c>IsOnlineImported = true</c>, weil die Online-Mediathek
    /// nur Serien anzeigt, die explizit über "Suche → Import" hinzugefügt wurden.
    /// Serien ohne Cover-Daten werden verwendet, um WinUI-3-Typen (BitmapImage) zu umgehen.
    /// </summary>
    public sealed class MediathekOnlineViewModelTests
    {
        private static MediathekOnlineViewModel BuildViewModel(FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => seriesService);

            // LoadAsync liest auch Episode-, PlaybackState- und AppSettings-Daten
            services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new EchoPlay.Data.Entities.Settings.AppSettings { ActiveProvider = EchoPlay.Data.Entities.Settings.ProviderType.Spotify }));

            // ImportService + Keyed Services für die Provider-Suche
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([]));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            services.AddSingleton<EchoPlay.App.Services.CoverService>();
            services.AddSingleton<EchoPlay.App.Services.EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            EchoPlay.App.Services.ImportService importService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EchoPlay.App.Services.EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            return new MediathekOnlineViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeConfirmationDialogService(),
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                new FakeOnlineAccessGuard());
        }

        [Fact]
        public async Task LoadAsync_SetsSeriesCount()
        {
            // Nur Serien mit Online-Quelle erscheinen in der Online-Mediathek
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG",                  SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Die drei Fragezeichen", SpotifyArtistId = "sp_3f", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "FAMOUS FIVE",           SpotifyArtistId = "sp_ff", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Equal(3, vm.Series.Count);
        }

        [Fact]
        public async Task LoadAsync_ExcludesLocalOnlySeries()
        {
            // Serien ohne SpotifyArtistId und AppleMusicArtistId (lokal per Scanner angelegt)
            // dürfen in der Online-Mediathek nicht erscheinen.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Online-Serie",  SpotifyArtistId = "sp_online", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Lokale Serie"   /* kein ArtistId */ });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Single(vm.Series);
            Assert.Equal("Online-Serie", vm.Series[0].Title);
        }

        [Fact]
        public async Task LoadAsync_MapsTitleCorrectly()
        {
            // Der Titel der Kachel muss dem Titel aus der DB entsprechen
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Equal("TKKG", vm.Series[0].Title);
        }

        [Fact]
        public async Task LoadAsync_MapsIdCorrectly()
        {
            // Die ID der Kachel muss der DB-ID entsprechen
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Equal(seriesService.All[0].Id, vm.Series[0].Id);
        }

        [Fact]
        public async Task LoadAsync_ReturnsEmptyList_WhenDatabaseIsEmpty()
        {
            // Leere Datenbank → leere Kachelliste, kein Fehler
            FakeSeriesDataService seriesService = new();

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Empty(vm.Series);
        }

        [Fact]
        public async Task LoadAsync_SetsIsLoading_FalseAfterCompletion()
        {
            // Nach dem Ladevorgang muss IsLoading wieder false sein
            FakeSeriesDataService seriesService = new();

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.False(vm.IsLoading);
        }

        [Fact]
        public async Task LoadAsync_SetsCoverImage_NullWhenNoCoverData()
        {
            // Series ohne CoverImageUrl und ohne LocalCoverData → CoverImage ist null
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Ohne Cover", SpotifyArtistId = "sp_oc", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Null(vm.Series[0].CoverImage);
        }

        [Fact]
        public async Task LoadAsync_CanBeCalledMultipleTimes()
        {
            // Mehrfaches Laden überschreibt das Ergebnis korrekt
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();
            await vm.LoadAsync();

            // Zweiter Aufruf darf nicht doppelt stapeln
            Assert.Single(vm.Series);
        }

        // --- Suchfilter-Tests ---

        [Fact]
        public async Task SearchText_ShowsAllSeries_WhenEmpty()
        {
            // Leerer Suchtext → alle Online-Serien sichtbar
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG",                  SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Die drei Fragezeichen", SpotifyArtistId = "sp_3f", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();
            vm.SearchText = string.Empty;

            Assert.Equal(2, vm.Series.Count);
        }

        [Fact]
        public async Task SearchText_FiltersByTitle_CaseInsensitive()
        {
            // Kleingeschriebener Suchtext trifft gemischten Titel
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG",                  SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Die drei Fragezeichen", SpotifyArtistId = "sp_3f", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Famous Five",           SpotifyArtistId = "sp_ff", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();
            vm.SearchText = "drei";

            Assert.Single(vm.Series);
            Assert.Equal("Die drei Fragezeichen", vm.Series[0].Title);
        }

        [Fact]
        public async Task SearchText_ReturnsEmpty_WhenNoMatch()
        {
            // Kein Treffer → leere gefilterte Liste
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();
            vm.SearchText = "XXXXXX";

            Assert.Empty(vm.Series);
        }

        [Fact]
        public async Task SearchText_ResetsFilter_WhenCleared()
        {
            // Suchtext leeren stellt alle Serien wieder her
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG",           SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Bibi Blocksberg", SpotifyArtistId = "sp_bb", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();
            vm.SearchText = "TKKG";

            Assert.Single(vm.Series);

            vm.SearchText = string.Empty;

            Assert.Equal(2, vm.Series.Count);
        }

        [Fact]
        public async Task SearchText_WorksBeforeLoad_WithoutException()
        {
            // SearchText setzen vor LoadAsync darf keine Exception werfen
            FakeSeriesDataService seriesService = new();
            MediathekOnlineViewModel vm = BuildViewModel(seriesService);

            vm.SearchText = "Test";

            Assert.Empty(vm.Series);
        }

        // --- Statusfilter-Tests ---

        [Fact]
        public async Task StatusFilter_Neu_ShowsOnlySeriesWithNewEpisodes()
        {
            // Filter "Neu" zeigt nur Serien, bei denen noch nicht angehörte Episoden vorhanden sind
            FakeSeriesDataService seriesService   = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG",  SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });
            await seriesService.AddAsync(new Series { Title = "Globi", SpotifyArtistId = "sp_globi", IsOnlineImported = true });

            Guid tkkg  = seriesService.All.First(s => s.Title == "TKKG").Id;
            Guid globi = seriesService.All.First(s => s.Title == "Globi").Id;

            // TKKG: 1 ungehörte Episode – Fake-Zähler vorkonfigurieren, da der Fake
            // keine Episode-zu-Serie-Zuordnung kennt und keine Eigenberechnung möglich ist.
            FakePlaybackStateDataService stateServiceWithCounts = new(
                seriesCounts: new Dictionary<Guid, (int, int, int)>
                {
                    [tkkg]  = (0, 0, 1),  // 1 NotStarted
                    [globi] = (0, 0, 0)   // keine Episoden
                });

            // Episodenservice braucht den Eintrag nur, damit LoadAsync die Serie nicht ignoriert
            await episodeService.AddAsync(new Episode { Title = "Folge 1", SeriesId = tkkg });

            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => seriesService);
            services.AddScoped<IEpisodeDataService>(_ => episodeService);
            services.AddScoped<IPlaybackStateDataService>(_ => stateServiceWithCounts);
            services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new EchoPlay.Data.Entities.Settings.AppSettings { ActiveProvider = EchoPlay.Data.Entities.Settings.ProviderType.Spotify }));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([]));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            services.AddSingleton<EchoPlay.App.Services.CoverService>();
            services.AddSingleton<EchoPlay.App.Services.EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            EchoPlay.App.Services.ImportService importService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EchoPlay.App.Services.EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            MediathekOnlineViewModel vm = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeConfirmationDialogService(),
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                new FakeOnlineAccessGuard());

            await vm.LoadAsync();
            vm.StatusFilter = SeriesStatusFilter.Neu;

            Assert.Single(vm.Series);
            Assert.Equal("TKKG", vm.Series[0].Title);
        }

        [Fact]
        public async Task ToggleSubscription_UpdatesIsSubscribed()
        {
            // Abonnement-Toggle muss IsSubscribed auf der Kachel und in der DB umschalten
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = false, SpotifyArtistId = "sp_tkkg", IsOnlineImported = true });

            MediathekOnlineViewModel vm = BuildViewModel(seriesService);
            await vm.LoadAsync();

            Assert.Single(vm.Series);
            Assert.False(vm.Series[0].IsSubscribed);

            // Toggle auslösen – FakeConfirmationDialogService bestätigt automatisch
            vm.Series[0].ToggleSubscriptionCommand.Execute(null);

            Assert.True(vm.Series[0].IsSubscribed);
        }
    }
}
