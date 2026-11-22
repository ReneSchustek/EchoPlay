using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, dass ein beim Einlesen gefundenes Serien-Cover in der Ablage landet, für
    /// neu angelegte wie für bereits bekannte Serien.
    /// </summary>
    /// <remarks>
    /// Wird das Cover beim Einlesen übergangen, bleibt die Mediathek nach dem ersten Lauf
    /// eine Wand aus Platzhaltern, obwohl die Bilder auf der Platte liegen, und der
    /// Hintergrundlauf holt sie erst Minuten später nach.
    /// </remarks>
    public sealed class SyncServiceCoverResolveTests
    {
        private const string SeriesFolder = @"D:\Media\TKKG";
        private static readonly byte[] CoverBytes = [0x31, 0x32];

        [Fact]
        public async Task Sync_ForANewSeries_PutsTheFoundCoverIntoTheStore()
        {
            Fixture fixture = Build(new FakeSeriesDataService(), new FakeLocalCoverService(CoverBytes));

            _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(fixture.CoverService.StoredSeriesCovers);
            (string seriesFolder, string? coverUrl) = Assert.Single(fixture.LocalCovers.Calls);
            Assert.Equal(SeriesFolder, seriesFolder);
            Assert.Null(coverUrl);
        }

        [Fact]
        public async Task Sync_ForANewSeriesWithoutCoverOnDisk_StoresNothing()
        {
            Fixture fixture = Build(new FakeSeriesDataService(), new FakeLocalCoverService());

            _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(fixture.CoverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task Sync_ForAKnownSeries_PassesItsProviderUrlToTheResolver()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series
                {
                    Title = "TKKG",
                    LocalFolderPath = SeriesFolder,
                    CoverImageUrl = "https://i.example.invalid/tkkg.jpg",
                },
                TestContext.Current.CancellationToken);

            Fixture fixture = Build(seriesService, new FakeLocalCoverService(CoverBytes));

            _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Bei einer bekannten Serie darf die Adresse des Anbieters als zweite Quelle
            // mitgegeben werden, denn beim Neuanlegen gibt es sie noch gar nicht.
            (string _, string? coverUrl) = Assert.Single(fixture.LocalCovers.Calls);
            Assert.Equal("https://i.example.invalid/tkkg.jpg", coverUrl);
        }

        /// <summary>
        /// Dienst und Cover-Doubles eines Testfalls.
        /// </summary>
        private sealed class Fixture
        {
            public required SyncService Service { get; init; }
            public required FakeCoverService CoverService { get; init; }
            public required FakeLocalCoverService LocalCovers { get; init; }
        }

        private static Fixture Build(FakeSeriesDataService seriesService, FakeLocalCoverService localCovers)
        {
            LocalScanResult scanResult = new()
            {
                SeriesName = "TKKG",
                SeriesFolderPath = SeriesFolder,
                Episodes = [],
            };

            FakeLocalLibraryScanner scanner = new([scanResult]);
            FakeCoverService coverService = new();

            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = @"D:\Media",
            });

            return new Fixture
            {
                Service = new SyncServiceSetup
                {
                    Settings = settings,
                    Series = seriesService,
                    Scanner = scanner,
                    LocalCovers = localCovers,
                }.Build(coverService),
                CoverService = coverService,
                LocalCovers = localCovers,
            };
        }
    }
}
