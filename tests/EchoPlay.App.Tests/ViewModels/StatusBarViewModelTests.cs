using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="StatusBarViewModel"/>.
    /// Prüft die korrekten Statistiken in der Info-Leiste des Hauptfensters.
    /// </summary>
    public sealed class StatusBarViewModelTests
    {
        private static (StatusBarViewModel Vm, FakePlaybackStateDataService StateService) BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakePlaybackStateDataService stateService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => stateService);
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            StatusBarViewModel vm = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService(),
                new FakeClock());

            return (vm, stateService);
        }

        [Fact]
        public async Task LoadAsync_CountsSubscribedSeries()
        {
            // Nur abonnierte Serien zählen für die Info-Leiste
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
            await seriesService.AddAsync(new Series { Title = "Bibi", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
            await seriesService.AddAsync(new Series { Title = "Globi", IsSubscribed = false }, cancellationToken: TestContext.Current.CancellationToken);

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                new FakeEpisodeDataService(),
                new FakePlaybackStateDataService());

            await vm.LoadAsync();

            Assert.Equal(2, vm.SubscribedSeriesCount);
        }

        [Fact]
        public async Task LoadAsync_CountsFinishedEpisodes()
        {
            // Episoden mit IsCompleted=true werden als gehört gezählt
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
            Guid seriesId = seriesService.All[0].Id;

            Episode ep1 = new() { Title = "Folge 1", SeriesId = seriesId };
            Episode ep2 = new() { Title = "Folge 2", SeriesId = seriesId };
            Episode ep3 = new() { Title = "Folge 3", SeriesId = seriesId };
            await episodeService.AddAsync(ep1, cancellationToken: TestContext.Current.CancellationToken);
            await episodeService.AddAsync(ep2, cancellationToken: TestContext.Current.CancellationToken);
            await episodeService.AddAsync(ep3, cancellationToken: TestContext.Current.CancellationToken);

            List<PlaybackState> states =
            [
                new PlaybackState { EpisodeId = ep1.Id, IsCompleted = true,  LastPosition = TimeSpan.Zero },
                new PlaybackState { EpisodeId = ep2.Id, IsCompleted = true,  LastPosition = TimeSpan.Zero },
                new PlaybackState { EpisodeId = ep3.Id, IsCompleted = false, LastPosition = TimeSpan.FromSeconds(30) },
            ];

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                episodeService,
                new FakePlaybackStateDataService(states));

            await vm.LoadAsync();

            Assert.Equal(2, vm.FinishedEpisodesCount);
            Assert.Equal(1, vm.UnfinishedEpisodesCount);
        }

        [Fact]
        public async Task LoadAsync_CountsNewEpisodes()
        {
            // "Neu" = erschienen (ReleaseDate ≤ heute), aber noch nicht gehört
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "Die drei ???", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
            Guid seriesId = seriesService.All[0].Id;

            // Gestern erschienen, noch nicht gehört → zählt als neu
            Episode epNeu = new()
            {
                Title = "Folge 1",
                SeriesId = seriesId,
                ReleaseDate = TestIds.ReferenceDate.AddDays(-1)
            };
            // Zukünftig → zählt nicht als neu
            Episode epKommend = new()
            {
                Title = "Folge 2",
                SeriesId = seriesId,
                ReleaseDate = TestIds.ReferenceDate.AddDays(7)
            };

            await episodeService.AddAsync(epNeu, cancellationToken: TestContext.Current.CancellationToken);
            await episodeService.AddAsync(epKommend, cancellationToken: TestContext.Current.CancellationToken);

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                episodeService,
                new FakePlaybackStateDataService());

            await vm.LoadAsync();

            Assert.Equal(1, vm.NewEpisodesCount);
        }

        [Fact]
        public async Task RefreshAsync_UpdatesAfterStatusChange()
        {
            // Nach einer Statusänderung muss RefreshAsync die Zähler aktualisieren
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
            Guid seriesId = seriesService.All[0].Id;

            Episode ep = new() { Title = "Folge 1", SeriesId = seriesId };
            await episodeService.AddAsync(ep, cancellationToken: TestContext.Current.CancellationToken);

            (StatusBarViewModel vm, _) = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            // Vor dem Refresh: keine abgeschlossene Episode
            Assert.Equal(0, vm.FinishedEpisodesCount);

            // Status nachtragen und Statistik aktualisieren
            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = ep.Id,
                IsCompleted = true,
                LastPosition = TimeSpan.Zero
            }, cancellationToken: TestContext.Current.CancellationToken);

            await vm.RefreshAsync();

            Assert.Equal(1, vm.FinishedEpisodesCount);
        }

        private static StatusBarViewModel BuildViewModelWithProvider(
            ProviderType provider,
            ISpotifyClientCredentialsProvider? credentialsProvider)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ =>
                new FakeAppSettingsDataService(new AppSettings { ActiveProvider = provider }));
            _ = services.AddSingleton<ILocalizationService>(new FakeLocalizationService());
            if (credentialsProvider is not null)
            {
                _ = services.AddSingleton(credentialsProvider);
            }

            ServiceProvider sp = services.BuildServiceProvider();
            return new StatusBarViewModel(
                sp.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService(),
                new FakeClock());
        }

        [Fact]
        public async Task LoadAsync_SpotifyWithoutCredentials_ShowsDisconnectedNotSpotify()
        {
            // ActiveProvider=Spotify, aber keine Credentials → die Suche läuft effektiv
            // über Apple Music (ImportService.ResolveProviderAsync). Die Info-Leiste darf dann nicht
            // fälschlich "Spotify" als verbunden zeigen. FakeLocalizationService.Get liefert den Key.
            StatusBarViewModel vm = BuildViewModelWithProvider(
                ProviderType.Spotify, FakeSpotifyClientCredentialsProvider.Missing());

            await vm.LoadAsync();

            Assert.Equal("StatusBarSpotifyDisconnected", vm.ActiveProviderDisplay);
        }

        [Fact]
        public async Task LoadAsync_SpotifyWithCredentials_ShowsSpotify()
        {
            StatusBarViewModel vm = BuildViewModelWithProvider(
                ProviderType.Spotify, FakeSpotifyClientCredentialsProvider.WithCredentials());

            await vm.LoadAsync();

            Assert.Equal("Spotify", vm.ActiveProviderDisplay);
        }

        [Fact]
        public async Task LoadAsync_AppleMusic_ShowsAppleMusicRegardlessOfCredentials()
        {
            // Apple Music ist nicht credential-abhängig – Anzeige bleibt unverändert.
            StatusBarViewModel vm = BuildViewModelWithProvider(
                ProviderType.AppleMusic, credentialsProvider: null);

            await vm.LoadAsync();

            Assert.Equal("Apple Music", vm.ActiveProviderDisplay);
        }
    }
}
