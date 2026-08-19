using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, wie der Folgenprüfer eine ganze Reihe von Serien abarbeitet: was er
    /// überspringt, was er trotz Fehler weiterführt und wie er die Anfragen bremst.
    /// </summary>
    /// <remarks>
    /// Die Prüfung läuft über alle beobachteten Serien. Bricht sie beim ersten Fehler ab,
    /// bekommt der Anwender für den halben Bestand keine Neuerscheinungen mehr — und merkt
    /// es nicht, weil ein leerer Abschnitt aussieht wie „nichts Neues".
    /// </remarks>
    public sealed class OnlineEpisodeCheckerBatchTests
    {
        private const long ArtistId = 4711;

        [Fact]
        public async Task CheckAll_WithoutAResolvableArtist_SkipsTheSeries()
        {
            FakeAppleMusicSearchClient client = new();
            OnlineEpisodeChecker sut = Build(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckAllAsync(
                [Checkable(artistId: null, title: "Unbekannte Serie")],
                TestContext.Current.CancellationToken);

            // Ohne Künstler-Kennung gibt es beim Anbieter nichts nachzuschlagen.
            Assert.Empty(results);
        }

        [Fact]
        public async Task CheckAll_WhenTheArtistHasNoAlbums_SkipsTheSeries()
        {
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>> { [ArtistId] = [] });
            OnlineEpisodeChecker sut = Build(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckAllAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "TKKG")],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task CheckAll_WhenOneSeriesFails_KeepsCheckingTheOthers()
        {
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>(),
                lookupFailure: () => new InvalidOperationException("Anbieter antwortet nicht"));
            OnlineEpisodeChecker sut = Build(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckAllAsync(
                [
                    Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "TKKG"),
                    Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "Bibi Blocksberg"),
                ],
                TestContext.Current.CancellationToken);

            // Beide Serien wurden angefragt, obwohl die erste geworfen hat.
            Assert.Empty(results);
            Assert.Equal(2, client.LookupAlbumsCallCount);
        }

        [Fact]
        public async Task CheckNewReleases_WithAFutureReleaseDate_ListsItAsAnnounced()
        {
            FakeClock clock = new();
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("TKKG - Folge 250 - Der neue Fall", clock.UtcNow.AddDays(20))],
                });
            OnlineEpisodeChecker sut = Build(client, clock);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "TKKG")],
                clock.UtcNow.AddDays(-30),
                TestContext.Current.CancellationToken);

            // Eine Folge mit Erscheinungsdatum in der Zukunft wird zusätzlich als
            // Ankündigung geführt — daran unterscheidet die Startseite „schon da“ von
            // „kommt noch“.
            OnlineEpisodeCheckResult result = Assert.Single(results);
            _ = Assert.Single(result.AnnouncedEpisodes);
            Assert.Equal(
                "TKKG - Folge 250 - Der neue Fall",
                result.AnnouncedEpisodes[0].Title);
        }

        [Fact]
        public async Task CheckNewReleases_WithARecentReleaseDate_ListsItAsNew()
        {
            FakeClock clock = new();
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] = [Album("TKKG - Folge 249 - Der letzte Fall", clock.UtcNow.AddDays(-3))],
                });
            OnlineEpisodeChecker sut = Build(client, clock);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckNewReleasesAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "TKKG")],
                clock.UtcNow.AddDays(-30),
                TestContext.Current.CancellationToken);

            OnlineEpisodeCheckResult result = Assert.Single(results);
            _ = Assert.Single(result.NewReleaseEpisodes);

            // Eine bereits erschienene Folge ist keine Ankündigung.
            Assert.Empty(result.AnnouncedEpisodes);
        }

        [Fact]
        public async Task CheckAll_WithAnAlbumDatedInTheFuture_MarksItAsAnnounced()
        {
            FakeClock clock = new();
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>>
                {
                    [ArtistId] =
                    [
                        Album("TKKG - Folge 250 - Der neue Fall", clock.UtcNow.AddDays(30)),
                        Album("TKKG - Folge 249 - Der letzte Fall", clock.UtcNow.AddDays(-30)),
                    ],
                });
            OnlineEpisodeChecker sut = Build(client, clock);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await sut.CheckAllAsync(
                [Checkable(ArtistId.ToString(CultureInfo.InvariantCulture), "TKKG")],
                TestContext.Current.CancellationToken);

            // Auch im Bericht über fehlende Folgen zählt eine noch nicht erschienene Folge
            // als Ankündigung — wer sie sucht, findet sie sonst nirgends.
            OnlineEpisodeCheckResult result = Assert.Single(results);
            _ = Assert.Single(result.AnnouncedEpisodes);
            Assert.Equal(250, result.OnlineHighestNumber);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static OnlineEpisodeChecker Build(
            FakeAppleMusicSearchClient client, FakeClock? clock = null)
            => new(client, new FakeSeriesDataService(), new FakeLoggerFactory(), clock ?? new FakeClock());

        private static ITunesCollectionDto Album(string name, DateTime? releaseDate = null) => new()
        {
            WrapperType = "collection",
            CollectionId = 1,
            CollectionName = name,
            ArtistId = ArtistId,
            ArtistName = "TKKG",
            ReleaseDate = releaseDate?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        private static CheckableSeriesInfo Checkable(string? artistId, string title) => new()
        {
            SeriesId = TestIds.SeriesA,
            Title = title,
            AppleMusicArtistId = artistId,
        };
    }
}
