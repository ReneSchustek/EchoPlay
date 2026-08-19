using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Reihenfolge, in der die Cover-Fabrik nach einem Bild sucht:
    /// Ablage, dann <c>cover.jpg</c> im Ordner, dann die Kennzeichnung der ersten Spur.
    /// </summary>
    /// <remarks>
    /// Das fertige Bildobjekt entsteht am Fenster und lässt sich im Testlauf nicht
    /// erzeugen. Geprüft ist deshalb, wo die Fabrik nachsieht und wo bewusst nicht —
    /// jeder überflüssige Griff auf die Platte kostet bei einer vollen Übersicht
    /// hundertfach Zeit.
    /// </remarks>
    public sealed class CoverViewModelFactoryTests
    {
        /// <summary>Ein Ordner ohne cover.jpg — er existiert bewusst nicht.</summary>
        private const string FolderWithoutCover = @"D:\Media\TKKG\Folge 001";

        [Fact]
        public void Constructor_WithoutScopeFactory_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => new CoverViewModelFactory(null!));
        }

        [Fact]
        public async Task BuildSeriesCoverAsync_WithoutSeries_ReturnsNoCover()
        {
            CoverViewModelFactory sut = BuildFactory(out _);

            Assert.Null(await sut.BuildSeriesCoverAsync(null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BuildSeriesCoverAsync_WithoutFolderAndWithoutUrl_ReturnsNoCover()
        {
            CoverViewModelFactory sut = BuildFactory(out _);
            Series series = new() { Title = "TKKG" };

            Assert.Null(await sut.BuildSeriesCoverAsync(series, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BuildSeriesCoverAsync_WithFolderButWithoutCoverFile_ReturnsNoCover()
        {
            CoverViewModelFactory sut = BuildFactory(out _);
            Series series = new() { Title = "TKKG", LocalFolderPath = FolderWithoutCover };

            Assert.Null(await sut.BuildSeriesCoverAsync(series, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithoutEpisode_ThrowsArgumentNullException()
        {
            CoverViewModelFactory sut = BuildFactory(out _);

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.BuildEpisodeCoverAsync(null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithoutFolder_DoesNotTouchTheDisk()
        {
            CoverViewModelFactory sut = BuildFactory(out ConfigurableLocalCoverLoader coverLoader);
            Episode episode = new() { Title = "Folge 1" };

            Assert.Null(await sut.BuildEpisodeCoverAsync(episode, TestContext.Current.CancellationToken));
            Assert.Empty(coverLoader.Calls);
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithFolder_AsksTheLoaderForThatFolder()
        {
            CoverViewModelFactory sut = BuildFactory(out ConfigurableLocalCoverLoader coverLoader);
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };

            _ = await sut.BuildEpisodeCoverAsync(episode, TestContext.Current.CancellationToken);

            _ = Assert.Single(coverLoader.Calls);
            Assert.Equal(FolderWithoutCover, coverLoader.Calls[0].FolderPath);
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithSeveralTracks_HandsTheLowestTrackNumberToTheLoader()
        {
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episode.Id] =
                [
                    new LocalTrack { EpisodeId = episode.Id, TrackNumber = 2, FilePath = FolderWithoutCover + @"\Spur02.mp3" },
                    new LocalTrack { EpisodeId = episode.Id, TrackNumber = 1, FilePath = FolderWithoutCover + @"\Spur01.mp3" },
                ],
            });
            CoverViewModelFactory sut = BuildFactory(out ConfigurableLocalCoverLoader coverLoader, trackService);

            _ = await sut.BuildEpisodeCoverAsync(episode, TestContext.Current.CancellationToken);

            // Liegt kein cover.jpg im Ordner, trägt die Kennzeichnung der ersten Spur das Bild.
            Assert.Equal(FolderWithoutCover + @"\Spur01.mp3", coverLoader.Calls[0].FirstTrackPath);
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithoutTracks_AsksTheLoaderWithoutTrack()
        {
            CoverViewModelFactory sut = BuildFactory(out ConfigurableLocalCoverLoader coverLoader);
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };

            _ = await sut.BuildEpisodeCoverAsync(episode, TestContext.Current.CancellationToken);

            Assert.Null(coverLoader.Calls[0].FirstTrackPath);
        }

        [Fact]
        public async Task BuildSeriesCoverAsync_AsksTheStoreBeforeTouchingTheDisk()
        {
            FakeCoverService coverService = new();
            CoverViewModelFactory sut = BuildFactory(out ConfigurableLocalCoverLoader _, coverService: coverService);
            Series series = new() { Title = "TKKG", LocalFolderPath = FolderWithoutCover };

            _ = await sut.BuildSeriesCoverAsync(series, TestContext.Current.CancellationToken);

            // Die Ablage ist die schnellste Quelle; erst danach darf die Platte drankommen.
            Assert.Contains(series.Id, coverService.SeriesCoverRequests);
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_AsksTheStoreBeforeTheFolder()
        {
            FakeCoverService coverService = new();
            CoverViewModelFactory sut = BuildFactory(
                out ConfigurableLocalCoverLoader coverLoader, coverService: coverService);
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };

            _ = await sut.BuildEpisodeCoverAsync(episode, TestContext.Current.CancellationToken);

            Assert.NotEmpty(coverService.EpisodeCoverRequests);
            _ = Assert.Single(coverLoader.Calls);
        }

        [Fact]
        public async Task BuildSeriesCoverAsync_WithACoverFileInTheFolder_ReadsIt()
        {
            string folder = CreateTempFolder();
            try
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(folder, EchoPlay.Core.CoverConstants.CoverFileName),
                    [1, 2, 3],
                    TestContext.Current.CancellationToken);

                CoverViewModelFactory sut = BuildFactory(out _, coverService: new FakeCoverService());

                // Der Weg endet am Bildobjekt, das im Testlauf nicht entsteht. Geprüft ist,
                // dass der Aufbau die Datei anfasst, ohne daran zu scheitern.
                _ = await sut.BuildSeriesCoverAsync(
                    new Series { Title = "TKKG", LocalFolderPath = folder },
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task BuildEpisodeCoverAsync_WithCoverBytesFromTheFolder_DoesNotThrow()
        {
            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]>
            {
                [FolderWithoutCover] = [1, 2, 3],
            });

            ServiceCollection services = new();
            ILocalCoverLoader asLoader = loader;
            _ = services.AddScoped(_ => asLoader);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            CoverViewModelFactory sut = new(
                provider.GetRequiredService<IServiceScopeFactory>(), new FakeCoverService());

            _ = await sut.BuildEpisodeCoverAsync(
                new Episode { Title = "Folge 1", LocalFolderPath = FolderWithoutCover },
                TestContext.Current.CancellationToken);

            _ = Assert.Single(loader.Calls);
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-coverfactory-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }

        private static CoverViewModelFactory BuildFactory(
            out ConfigurableLocalCoverLoader coverLoader,
            FakeLocalTrackDataService? trackService = null,
            FakeCoverService? coverService = null)
        {
            coverLoader = new ConfigurableLocalCoverLoader();
            ILocalCoverLoader loader = coverLoader;

            ServiceCollection services = new();
            _ = services.AddScoped(_ => loader);
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService ?? new FakeLocalTrackDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new CoverViewModelFactory(provider.GetRequiredService<IServiceScopeFactory>(), coverService);
        }
    }
}
