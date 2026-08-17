using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Nachlade-Phasen, die ohne Netz auskommen: Cover aus dem Dateisystem und
    /// die Kopie von lokalen auf online bezogene Folgen.
    /// </summary>
    /// <remarks>
    /// Die Zusage dieser Klasse ist, dass sie im Startbild ungebremst laufen darf — kein
    /// Wartelimit, keine Zugangsdaten, kein Netz. Die Tests halten die Regeln fest, die das
    /// tragen: Es wird nur geladen, was fehlt, und nur dort, wo ein Ordner hinterlegt ist.
    /// </remarks>
    public sealed class LocalCoverPhasesTests
    {
        private const string SeriesFolder = @"D:\Media\Die drei Fragezeichen";
        private const string EpisodeFolder = @"D:\Media\Die drei Fragezeichen\Folge 001";
        private const string SeriesTitle = "Die drei Fragezeichen";
        private static readonly byte[] CoverBytes = [0x42, 0x4D, 0x00, 0x01];

        [Fact]
        public async Task LoadMissingLocalSeriesCovers_SeriesWithoutFolder_IsSkipped()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);
            _ = await AddSeriesAsync(seriesService, "Ohne Ordner", localFolderPath: null);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [SeriesFolder] = CoverBytes });
            LocalCoverPhases phases = BuildPhases(seriesService, new FakeCoverService(), loader);

            _ = await phases.LoadMissingLocalSeriesCoversAsync(TestContext.Current.CancellationToken);

            // Ohne Ordner gibt es nichts zu lesen — die Serie darf gar nicht erst beim
            // Lader landen, sonst sucht der Startlauf im Nichts.
            Assert.Equal([SeriesFolder], loader.Calls.Select(call => call.FolderPath));
        }

        [Fact]
        public async Task LoadMissingLocalSeriesCovers_ForSeriesCovers_PassesNoTrackPath()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            ConfigurableLocalCoverLoader loader = new();
            LocalCoverPhases phases = BuildPhases(seriesService, new FakeCoverService(), loader);

            _ = await phases.LoadMissingLocalSeriesCoversAsync(TestContext.Current.CancellationToken);

            // Serien-Cover liegen als Datei im Stammordner. Ein Rückgriff auf die
            // Kennzeichnung des ersten Titels wäre hier falsch: Das Bild darin gehört
            // zur Folge, nicht zur Serie.
            Assert.Null(Assert.Single(loader.Calls).FirstTrackPath);
        }

        [Fact]
        public async Task LoadMissingLocalSeriesCovers_WhenCoverAlreadyStored_ReadsNothingFromDisk()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Series, seriesId, CoverBytes, cancellationToken: TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [SeriesFolder] = CoverBytes });
            LocalCoverPhases phases = BuildPhases(seriesService, new FakeCoverService(), loader, coverImages);

            int loaded = await phases.LoadMissingLocalSeriesCoversAsync(TestContext.Current.CancellationToken);

            // Was schon in der Ablage liegt, wird nicht erneut von der Platte gelesen.
            Assert.Equal(0, loaded);
            Assert.Empty(loader.Calls);
        }

        [Fact]
        public async Task LoadMissingLocalSeriesCovers_WhenCoverFound_StoresItAndCounts()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [SeriesFolder] = CoverBytes });
            FakeCoverService coverService = new();
            LocalCoverPhases phases = BuildPhases(seriesService, coverService, loader);

            int loaded = await phases.LoadMissingLocalSeriesCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, loaded);
            Assert.Equal([seriesId], coverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task LoadMissingLocalSeriesCovers_WhenCancelled_StoresNothing()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [SeriesFolder] = CoverBytes });
            FakeCoverService coverService = new();
            LocalCoverPhases phases = BuildPhases(seriesService, coverService, loader);

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            int loaded = await phases.LoadMissingLocalSeriesCoversAsync(cancelled.Token);

            // Der Abbruch kommt aus dem Startbild, wenn der Anwender weiterklickt. Er muss
            // vor dem Schreiben greifen, nicht erst danach.
            Assert.Equal(0, loaded);
            Assert.Empty(coverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task LoadMissingLocalEpisodeCovers_UsesFirstTrackAsFallbackSource()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            FakeEpisodeDataService episodeService = new();
            Guid episodeId = await AddEpisodeAsync(episodeService, seriesId, EpisodeFolder);

            const string trackPath = @"D:\Media\Die drei Fragezeichen\Folge 001\01.mp3";
            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episodeId] = [new LocalTrack { FilePath = trackPath }],
            });

            ConfigurableLocalCoverLoader loader = new();
            LocalCoverPhases phases = BuildPhases(
                seriesService, new FakeCoverService(), loader, episodeService: episodeService, trackService: trackService);

            _ = await phases.LoadMissingLocalEpisodeCoversAsync(TestContext.Current.CancellationToken);

            // Findet sich keine Bilddatei im Ordner, ist die Kennzeichnung des ersten Titels
            // die zweite Quelle. Ohne diesen Pfad bliebe die Kachel leer, obwohl das Bild
            // in der Datei steckt.
            Assert.Equal(trackPath, Assert.Single(loader.Calls).FirstTrackPath);
        }

        [Fact]
        public async Task LoadMissingLocalEpisodeCovers_EpisodeWithoutFolder_IsSkipped()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            FakeEpisodeDataService episodeService = new();
            _ = await AddEpisodeAsync(episodeService, seriesId, EpisodeFolder);
            _ = await AddEpisodeAsync(episodeService, seriesId, localFolderPath: null);

            ConfigurableLocalCoverLoader loader = new();
            LocalCoverPhases phases = BuildPhases(
                seriesService, new FakeCoverService(), loader, episodeService: episodeService);

            _ = await phases.LoadMissingLocalEpisodeCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal([EpisodeFolder], loader.Calls.Select(call => call.FolderPath));
        }

        [Fact]
        public async Task LoadMissingLocalEpisodeCovers_WhenNoCandidates_ReturnsZero()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            ConfigurableLocalCoverLoader loader = new();
            LocalCoverPhases phases = BuildPhases(seriesService, new FakeCoverService(), loader);

            int loaded = await phases.LoadMissingLocalEpisodeCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, loaded);
            Assert.Empty(loader.Calls);
        }

        [Fact]
        public async Task LoadMissingLocalEpisodeCovers_WhenCoverFound_StoresItForThatEpisode()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            FakeEpisodeDataService episodeService = new();
            Guid episodeId = await AddEpisodeAsync(episodeService, seriesId, EpisodeFolder);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [EpisodeFolder] = CoverBytes });
            FakeCoverService coverService = new();
            LocalCoverPhases phases = BuildPhases(
                seriesService, coverService, loader, episodeService: episodeService);

            int loaded = await phases.LoadMissingLocalEpisodeCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, loaded);
            Assert.Equal([episodeId], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task CopyLocalToOnline_OnlyTouchesSeriesImportedFromAProvider()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, "Nur lokal", SeriesFolder);
            _ = await AddSeriesAsync(seriesService, "Online bezogen", localFolderPath: null, isOnlineImported: true);

            FakeCoverCopyService copyService = new();
            LocalCoverPhases phases = BuildPhases(
                seriesService, new FakeCoverService(), new ConfigurableLocalCoverLoader(), copyService: copyService);

            _ = await phases.CopyLocalToOnlineAsync(TestContext.Current.CancellationToken);

            // Die Kopie füllt Folgen, die über einen Anbieter kamen und deshalb kein eigenes
            // Bild auf der Platte haben. Rein lokale Serien haben ihres bereits.
            Assert.Equal(1, copyService.CallCount);
        }

        [Fact]
        public async Task EnsureLocalCoversForSeries_MatchesTitleIgnoringCase()
        {
            FakeSeriesDataService seriesService = new();
            Guid seriesId = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            FakeEpisodeDataService episodeService = new();
            Guid episodeId = await AddEpisodeAsync(episodeService, seriesId, EpisodeFolder);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [EpisodeFolder] = CoverBytes });
            FakeCoverService coverService = new();
            LocalCoverPhases phases = BuildPhases(
                seriesService, coverService, loader, episodeService: episodeService);

            // Der Titel kommt aus der Anzeige und trägt die Schreibweise des Anwenders.
            int loaded = await phases.EnsureLocalCoversForSeriesAsync(
                "die DREI fragezeichen", TestContext.Current.CancellationToken);

            Assert.Equal(1, loaded);
            Assert.Equal([episodeId], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task EnsureLocalCoversForSeries_UnknownTitle_LoadsNothing()
        {
            FakeSeriesDataService seriesService = new();
            _ = await AddSeriesAsync(seriesService, SeriesTitle, SeriesFolder);

            ConfigurableLocalCoverLoader loader = new();
            LocalCoverPhases phases = BuildPhases(seriesService, new FakeCoverService(), loader);

            int loaded = await phases.EnsureLocalCoversForSeriesAsync(
                "Eine Serie, die es nicht gibt", TestContext.Current.CancellationToken);

            Assert.Equal(0, loaded);
            Assert.Empty(loader.Calls);
        }

        /// <summary>
        /// Legt eine Serie an und liefert die Kennung, die der Fake dabei vergeben hat —
        /// die Entitäten setzen ihre Kennung nicht selbst, das tut sonst die Datenbank.
        /// </summary>
        private static async Task<Guid> AddSeriesAsync(
            FakeSeriesDataService seriesService,
            string title,
            string? localFolderPath,
            bool isOnlineImported = false)
        {
            Series series = new()
            {
                Title = title,
                LocalFolderPath = localFolderPath,
                IsOnlineImported = isOnlineImported,
            };

            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);
            return series.Id;
        }

        private static async Task<Guid> AddEpisodeAsync(
            FakeEpisodeDataService episodeService,
            Guid seriesId,
            string? localFolderPath)
        {
            Episode episode = new()
            {
                SeriesId = seriesId,
                Title = "Folge",
                LocalFolderPath = localFolderPath,
            };

            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);
            return episode.Id;
        }

        /// <summary>
        /// Baut die Phasen über einen echten Bereichs-Erzeuger. Die Klasse holt ihre Dienste
        /// je Aufruf aus einem eigenen Bereich — ein nachgebauter Erzeuger würde genau das
        /// nicht prüfen.
        /// </summary>
        private static LocalCoverPhases BuildPhases(
            FakeSeriesDataService seriesService,
            FakeCoverService coverService,
            ConfigurableLocalCoverLoader coverLoader,
            FakeCoverImageDataService? coverImages = null,
            FakeEpisodeDataService? episodeService = null,
            FakeLocalTrackDataService? trackService = null,
            FakeCoverCopyService? copyService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService ?? new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService ?? new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => coverLoader);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages ?? new FakeCoverImageDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => copyService ?? new FakeCoverCopyService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalCoverPhases(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService,
                new FakeLogger());
        }
    }
}
