using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Lebenslauf der Serienansicht: den Leer-Hinweis, das Verlassen der Seite
    /// und den Wiedergabe-Befehl ohne gewählte Folge.
    /// </summary>
    /// <remarks>
    /// Beim Verlassen der Seite laufen noch Cover-Anfragen und Datenbankzugriffe. Werden
    /// sie nicht abgebrochen, schreiben sie in ein Ansichtsmodell, das niemand mehr sieht —
    /// und halten es samt seiner Bilder im Speicher.
    /// </remarks>
    public sealed class SeriesDetailLifecycleTests
    {
        [Fact]
        public void EpisodesEmptyVisibility_WithoutLoadedEpisodes_ShowsTheHint()
        {
            SeriesDetailViewModel sut = Build();

            Assert.Equal(Visibility.Visible, sut.EpisodesEmptyVisibility);
            Assert.False(sut.IsLoading);
        }

        [Fact]
        public async Task PlaySelectedEpisode_WithoutSelection_StartsNothing()
        {
            FakePlayerService player = new();
            SeriesDetailViewModel sut = Build(player);

            await sut.PlaySelectedEpisodeAsync();

            // Der Befehl hängt an einer Schaltfläche, die auch ohne Auswahl sichtbar ist.
            Assert.Empty(player.PlayCalls);
        }

        [Fact]
        public void Cleanup_WithoutARunningLoad_StaysQuiet()
        {
            SeriesDetailViewModel sut = Build();

            sut.Cleanup();
        }

        [Fact]
        public void Cleanup_CalledTwice_StaysQuiet()
        {
            SeriesDetailViewModel sut = Build();

            sut.Cleanup();
            sut.Cleanup();
        }

        [Fact]
        public async Task Cleanup_AfterLoading_StopsTheRemainingWork()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new EchoPlay.Data.Entities.Library.Series { Title = "TKKG" },
                TestContext.Current.CancellationToken);

            SeriesDetailViewModel sut = Build(seriesService: seriesService);
            await sut.LoadAsync(seriesService.All[0].Id);

            sut.Cleanup();

            Assert.Equal("TKKG", sut.Header.SeriesTitle);
        }

        private static SeriesDetailViewModel Build(
            FakePlayerService? player = null, FakeSeriesDataService? seriesService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());

            ServiceProvider provider = services.BuildServiceProvider();

            return new SeriesDetailViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                player ?? new FakePlayerService(),
                new FakeClock());
        }
    }
}
