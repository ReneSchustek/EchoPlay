using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.AppleMusic.Tests.TestData;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.AppleMusic.Tests.Import
{
    /// <summary>
    /// Fachliche Tests für den Apple-Music-Episodenimport.
    /// Diese Tests prüfen ausschließlich das vertraglich zugesicherte Verhalten des IEpisodeImportSource.
    /// </summary>
    public sealed class AppleMusicEpisodeSourceTests
    {
        /// <summary>
        /// Baut den produktiven Import mit dem Such-Nachbau. AddAppleMusicImport muss vor dem
        /// Nachbau stehen, damit dessen Registrierung den produktiven Client überschreibt.
        /// </summary>
        private static IEpisodeImportSource CreateImport(IAppleMusicSearchClient client)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();
            _ = services.AddSingleton(client);

            return services.BuildServiceProvider().GetRequiredService<IEpisodeImportSource>();
        }

        /// <summary>Ein Hörspiel-Album mit einem Titel, wie es der Künstler-Lookup liefert.</summary>
        private static ITunesCollectionDto Album(
            long collectionId,
            string name,
            long artistId,
            string artistName,
            string? releaseDate = null,
            string? artworkUrl = null)
            => new()
            {
                WrapperType = "collection",
                CollectionId = collectionId,
                CollectionName = name,
                ArtistId = artistId,
                ArtistName = artistName,
                ReleaseDate = releaseDate,
                ArtworkUrl100 = artworkUrl,
                TrackCount = 1,
                PrimaryGenreName = "Hörspiele"
            };

        /// <summary>Die Kennung als Text, wie sie der Import erwartet.</summary>
        private static string Id(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Prüft, dass bei einer Serie ohne verfügbare Alben
        /// keine Episoden erzeugt werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_SeriesWithoutAlbums_ReturnsEmptyList()
        {
            // ARRANGE
            // Der Fake liefert nur Künstlerdaten.
            // Alben und Tracks sind nicht vorhanden.
            IEpisodeImportSource episodeImport = CreateImport(
                new FakeAppleMusicSearchClient(artists: [AppleMusicTestData.DieDreiFragezeichen]));

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("201306317", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Ohne Alben dürfen keine Episoden entstehen.
            Assert.Empty(episodes);
        }

        /// <summary>
        /// Lookup-Antworten der iTunes API können neben den eigenen Alben auch
        /// fremde Compilation-/Various-Artists-Einträge mit abweichender ArtistId enthalten.
        /// Diese dürfen nicht als Episoden der gesuchten Serie importiert werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_FiltersAlbumsWithForeignArtistId()
        {
            // ARRANGE
            const long ScotlandYardArtistId = 100100;
            const long DetektivClipperArtistId = 200200;

            FakeAppleMusicSearchClient client = new(artists: []);
            client.AddAlbums(ScotlandYardArtistId,
            [
                Album(1001, "Scotland Yard – Folge 1", ScotlandYardArtistId, "Scotland Yard"),
                Album(1002, "Scotland Yard – Folge 2", ScotlandYardArtistId, "Scotland Yard"),
                Album(9001, "Detektiv Clipper – Folge 7", DetektivClipperArtistId, "Detektiv Clipper")
            ]);

            IEpisodeImportSource episodeImport = CreateImport(client);

            // ACT
            IReadOnlyList<ImportEpisode> episodes =
                await episodeImport.GetEpisodesAsync(Id(ScotlandYardArtistId), cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Genau zwei Episoden, das fremde Detektiv-Clipper-Album darf nicht durchrutschen.
            Assert.Equal(2, episodes.Count);
            Assert.All(episodes, e => Assert.Contains("Scotland Yard", e.Title, StringComparison.Ordinal));
            Assert.DoesNotContain(episodes, e => e.Title.Contains("Detektiv Clipper", StringComparison.Ordinal));
        }

        /// <summary>
        /// Delta-Abgleich: Für bereits bekannte Folgen (Titel = Albumname) muss der teure Track-Lookup
        /// entfallen, aber ihre Metadaten (inkl. Cover) müssen weiterhin zurückkommen, damit der
        /// Delta-Import ein fehlendes Cover einer bestehenden Folge nachtragen kann. Nur für die
        /// unbekannte, neue Folge darf ein Track-Lookup ausgelöst werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_KnownTitles_SkipsTrackLookupButKeepsMetadata()
        {
            // ARRANGE
            const long ArtistId = 555000;
            const long KnownCollectionId = 4001;
            const long NewCollectionId = 4002;

            FakeAppleMusicSearchClient client = new(artists: []);
            client.AddAlbums(ArtistId,
            [
                Album(KnownCollectionId, "Folge 1", ArtistId, "TKKG",
                    "2020-01-01T00:00:00Z", "https://example.org/100x100/folge1.jpg"),
                Album(NewCollectionId, "Folge 2", ArtistId, "TKKG",
                    "2021-01-01T00:00:00Z", "https://example.org/100x100/folge2.jpg")
            ]);

            IEpisodeImportSource episodeImport = CreateImport(client);
            HashSet<string> known = new(StringComparer.OrdinalIgnoreCase) { "Folge 1" };

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync(
                Id(ArtistId), known, TestContext.Current.CancellationToken);

            // ASSERT
            // Beide Alben kommen zurück: Die bekannte "Folge 1" wird nicht verschluckt (Cover-Nachtrag).
            Assert.Equal(2, episodes.Count);
            Assert.Contains(episodes, e => e.Title == "Folge 1");
            Assert.Contains(episodes, e => e.Title == "Folge 2");
            // Bekannte "Folge 1" behält ihre Cover-URL aus den Album-Metadaten (kein Track-Lookup nötig).
            Assert.Contains(episodes, e => e.Title == "Folge 1" && e.CoverImageUrl is not null);

            // Der teure Track-Lookup lief NUR für die neue, unbekannte Folge, nicht für "Folge 1".
            Assert.Equal([NewCollectionId], client.LookedUpCollectionIds);
        }

        /// <summary>
        /// Prüft, dass eine ungültige SourceSeriesId zu einer ArgumentException führt.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_InvalidSourceSeriesId_ThrowsArgumentException()
        {
            // ARRANGE
            IEpisodeImportSource episodeImport = CreateImport(new FakeAppleMusicSearchClient(artists: []));

            // ACT & ASSERT
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => episodeImport.GetEpisodesAsync("keine-gültige-id", cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Die Track-Lookups laufen gebündelt (ein Batch-Request statt N Einzel-Lookups),
        /// die Episoden-Dauer wird korrekt aus den (über die CollectionId zugeordneten) Tracks
        /// aufsummiert. Sichert zu, dass die Performance-Umstellung die Dauer nicht verliert.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_BatchedTrackLookup_ComputesDurationPerAlbum()
        {
            // ARRANGE
            const long ArtistId = 777000;
            const long CollectionA = 7001;
            const long CollectionB = 7002;

            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(ArtistId,
                [
                    new ITunesCollectionDto { WrapperType = "collection", CollectionId = CollectionA, CollectionName = "Folge A", ArtistId = ArtistId, ReleaseDate = "2021-01-01T00:00:00Z" },
                    new ITunesCollectionDto { WrapperType = "collection", CollectionId = CollectionB, CollectionName = "Folge B", ArtistId = ArtistId, ReleaseDate = "2020-01-01T00:00:00Z" }
                ])
                .WithTracks(CollectionA,
                [
                    new ITunesTrackDto { WrapperType = "track", CollectionId = CollectionA, TrackTimeMillis = (int)TimeSpan.FromMinutes(10).TotalMilliseconds }
                ])
                .WithTracks(CollectionB,
                [
                    new ITunesTrackDto { WrapperType = "track", CollectionId = CollectionB, TrackTimeMillis = (int)TimeSpan.FromMinutes(20).TotalMilliseconds },
                    new ITunesTrackDto { WrapperType = "track", CollectionId = CollectionB, TrackTimeMillis = (int)TimeSpan.FromMinutes(5).TotalMilliseconds }
                ]);

            IEpisodeImportSource episodeImport = CreateImport(client);

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync(
                Id(ArtistId), cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            Assert.Equal(2, episodes.Count);
            ImportEpisode a = episodes.Single(e => e.SourceEpisodeId == Id(CollectionA));
            ImportEpisode b = episodes.Single(e => e.SourceEpisodeId == Id(CollectionB));
            Assert.Equal(TimeSpan.FromMinutes(10), a.Duration);
            Assert.Equal(TimeSpan.FromMinutes(25), b.Duration);
        }
    }
}
