using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Prüfablauf des <see cref="OnlineEpisodeChecker"/>: Artist-ID ermitteln,
    /// Alben laden, Nummern vergleichen. Die statischen Hilfsmethoden liegen in
    /// <see cref="OnlineEpisodeCheckerTests"/>.
    /// </summary>
    /// <remarks>
    /// Alle Tests arbeiten mit genau einer Serie. Der Checker pausiert zwischen zwei Serien
    /// bewusst 1,5 Sekunden wegen des iTunes-Limits — mehrere Serien pro Test würden die
    /// Laufzeit unnötig aufblähen, ohne mehr Verhalten zu prüfen.
    /// </remarks>
    public sealed class OnlineEpisodeCheckerFlowTests
    {
        private const long ArtistId = 4711;

        private static ITunesCollectionDto Album(string name, DateTime? releaseDate = null) => new()
        {
            WrapperType = "collection",
            CollectionId = 1,
            CollectionName = name,
            ArtistId = ArtistId,
            ArtistName = "Testserie",
            ReleaseDate = releaseDate?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        private static CheckableSeriesInfo Checkable(string? artistId, string? localFolder = null) => new()
        {
            SeriesId = TestIds.SeriesA,
            Title = "Testserie",
            AppleMusicArtistId = artistId,
            LocalFolderPath = localFolder
        };

        private static OnlineEpisodeChecker BuildChecker(
            FakeAppleMusicSearchClient client,
            FakeSeriesDataService? seriesService = null) =>
            new(client, seriesService ?? new FakeSeriesDataService(), new FakeLoggerFactory(), new FakeClock());

        [Fact]
        public async Task CheckAllAsync_WithoutArtistId_SearchesByTitle()
        {
            FakeAppleMusicSearchClient client = new(
                artists: [new ITunesArtistDto { WrapperType = "artist", ArtistId = ArtistId, ArtistName = "Testserie" }],
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Testserie - Folge 12 - Titel")]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(artistId: null)],
                TestContext.Current.CancellationToken);

            Assert.Equal(1, client.SearchArtistsCallCount);
            OnlineEpisodeCheckResult result = Assert.Single(results);
            Assert.Equal(12, result.OnlineHighestNumber);
        }

        [Fact]
        public async Task CheckAllAsync_WithKnownArtistId_SkipsSearch()
        {
            // Die gespeicherte ID spart einen HTTP-Aufruf – genau dafür wird sie hinterlegt.
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Folge 5 - Titel")]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            _ = await checker.CheckAllAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, client.SearchArtistsCallCount);
            Assert.Equal(1, client.LookupAlbumsCallCount);
        }

        [Fact]
        public async Task CheckAllAsync_WithoutArtistMatch_ReturnsNoResult()
        {
            FakeAppleMusicSearchClient client = new();
            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(artistId: null)],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task CheckAllAsync_WithoutAlbums_ReturnsNoResult()
        {
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>());

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task CheckAllAsync_WhenProviderThrows_SkipsSeriesInsteadOfFailing()
        {
            // Ein Fehler bei einer Serie darf den gesamten Durchlauf nicht abbrechen.
            FakeAppleMusicSearchClient client = new(
                lookupFailure: () => new InvalidOperationException("iTunes nicht erreichbar"));

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task CheckAllAsync_ComparesAgainstHighestLocalNumber()
        {
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Folge 8 - Acht"), Album("Folge 10 - Zehn")]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);
            string folder = CreateSeriesFolder("008 - Acht");

            try
            {
                IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                    [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), folder)],
                    TestContext.Current.CancellationToken);

                OnlineEpisodeCheckResult result = Assert.Single(results);
                Assert.Equal(8, result.LocalHighestNumber);
                Assert.Equal(10, result.OnlineHighestNumber);
                Assert.Contains(result.MissingOnlineEpisodes, e => e.EpisodeNumber == 10);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckNewReleasesAsync_OlderThanCutoff_IsIgnored()
        {
            // Alle Daten liegen vor dem Stand der Test-Uhr (TestIds.ReferenceDate). Ein
            // späteres Datum würde als Ankündigung gelten und wäre absichtlich enthalten.
            DateTime cutoff = TestIds.ReferenceDate.AddDays(-14);

            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Folge 3 - Alt", cutoff.AddDays(-30))]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                cutoff,
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(results, r => r.NewReleaseEpisodes.Count > 0);
        }

        [Fact]
        public async Task CheckNewReleasesAsync_WithinWindow_IsReported()
        {
            DateTime cutoff = TestIds.ReferenceDate.AddDays(-14);

            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Folge 42 - Neu", cutoff.AddDays(5))]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                cutoff,
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            Assert.Contains(result.NewReleaseEpisodes, e => e.EpisodeNumber == 42);
        }

        [Fact]
        public async Task CheckAllAsync_ResolvedArtistId_IsPersisted()
        {
            // Einmal gesucht, dauerhaft gemerkt: sonst kostet jede Prüfung eine Suchanfrage.
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Testserie", IsSubscribed = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeAppleMusicSearchClient client = new(
                artists: [new ITunesArtistDto { WrapperType = "artist", ArtistId = ArtistId, ArtistName = "Testserie" }],
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Folge 1 - Eins")]
                });

            OnlineEpisodeChecker checker = BuildChecker(client, seriesService);

            CheckableSeriesInfo checkable = new()
            {
                SeriesId = series.Id,
                Title = series.Title,
                AppleMusicArtistId = null
            };

            _ = await checker.CheckAllAsync([checkable], TestContext.Current.CancellationToken);

            Series? stored = await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken);
            Assert.Equal(ArtistId.ToString(CultureInfo.InvariantCulture), stored?.AppleMusicArtistId);
        }

        private static string CreateSeriesFolder(params string[] episodeFolderNames)
        {
            string root = Path.Combine(Path.GetTempPath(), $"echoplay-checker-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(root);

            foreach (string name in episodeFolderNames)
            {
                string episodeFolder = Path.Combine(root, name);
                _ = Directory.CreateDirectory(episodeFolder);
                File.WriteAllBytes(Path.Combine(episodeFolder, "track01.mp3"), []);
            }

            return root;
        }
        [Fact]
        public async Task Neuerscheinungen_NehmenNurAlbenImZeitfenster()
        {
            // Der Zeitraum ist die ganze Aussage der Prüfung: Was älter ist, hat der
            // Anwender längst gesehen — es gehört nicht als Neuerscheinung auf das Brett.
            DateTime heute = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] =
                    [
                        Album("Testserie - Folge 20 - Frisch", heute.AddDays(-3)),
                        Album("Testserie - Folge 10 - Alt", heute.AddDays(-90))
                    ]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                heute.AddDays(-30),
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            NewReleaseEpisode neu = Assert.Single(result.NewReleaseEpisodes);
            Assert.Equal("Testserie - Folge 20 - Frisch", neu.Title);
            Assert.Equal(20, neu.EpisodeNumber);
            Assert.Empty(result.AnnouncedEpisodes);
        }

        [Fact]
        public async Task Neuerscheinungen_ZeigenAngekuendigteFolgenMit()
        {
            // Eine angekündigte Folge steht zweimal im Ergebnis: als Ankündigung für den
            // Hinweis und als Neuerscheinung für die Kachel mit Erscheinungsdatum.
            DateTime heute = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Testserie - Folge 21 - Kommt noch", heute.AddDays(20))]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                heute.AddDays(-30),
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            Assert.Equal("Testserie - Folge 21 - Kommt noch", Assert.Single(result.AnnouncedEpisodes).Title);
            Assert.Equal(21, Assert.Single(result.NewReleaseEpisodes).EpisodeNumber);
            Assert.Equal(21, result.OnlineHighestNumber);
        }

        [Fact]
        public async Task Neuerscheinungen_SortierenNeuestesZuerst()
        {
            DateTime heute = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] =
                    [
                        Album("Testserie - Folge 18 - Älter", heute.AddDays(-20)),
                        Album("Testserie - Folge 19 - Neuer", heute.AddDays(-2))
                    ]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                heute.AddDays(-30),
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            Assert.Equal(
                ["Testserie - Folge 19 - Neuer", "Testserie - Folge 18 - Älter"],
                result.NewReleaseEpisodes.Select(e => e.Title));
        }

        [Fact]
        public async Task Neuerscheinungen_OhneTrefferMeldenDieSerieGarNicht()
        {
            // Eine Serie ohne Fund gehört nicht ins Ergebnis — sonst stünde sie mit
            // leerer Liste auf dem Brett und sähe aus wie ein Fehler.
            DateTime heute = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("Testserie - Folge 5 - Uralt", heute.AddDays(-400))]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                heute.AddDays(-30),
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task Neuerscheinungen_UebergehenAlbenOhneLesbaresDatum()
        {
            DateTime heute = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] =
                    [
                        Album("Testserie - Folge 30 - Ohne Datum"),
                        Album("Testserie - Folge 31 - Mit Datum", heute.AddDays(-1))
                    ]
                });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture))],
                heute.AddDays(-30),
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            Assert.Equal(31, Assert.Single(result.NewReleaseEpisodes).EpisodeNumber);
        }
    }
}
