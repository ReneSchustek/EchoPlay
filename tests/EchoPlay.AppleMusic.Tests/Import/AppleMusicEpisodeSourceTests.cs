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
        /// Stellt sicher, dass bei einer Serie ohne verfügbare Alben
        /// keine Episoden erzeugt werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_SeriesWithoutAlbums_ReturnsEmptyList()
        {
            // ARRANGE
            // Der Fake liefert bewusst nur Künstlerdaten.
            // Alben und Tracks sind nicht vorhanden.
            ServiceCollection services = new();

            // AddAppleMusicImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(
                    artists: [AppleMusicTestData.DieDreiFragezeichen]));

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

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
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            const long ScotlandYardArtistId = 100100;
            const long DetektivClipperArtistId = 200200;

            FakeAppleMusicSearchClient client = new(artists: []);
            client.AddAlbums(ScotlandYardArtistId,
            [
                new ITunesCollectionDto
                {
                    WrapperType = "collection",
                    CollectionId = 1001,
                    CollectionName = "Scotland Yard – Folge 1",
                    ArtistId = ScotlandYardArtistId,
                    ArtistName = "Scotland Yard",
                    TrackCount = 1,
                    PrimaryGenreName = "Hörspiele"
                },
                new ITunesCollectionDto
                {
                    WrapperType = "collection",
                    CollectionId = 1002,
                    CollectionName = "Scotland Yard – Folge 2",
                    ArtistId = ScotlandYardArtistId,
                    ArtistName = "Scotland Yard",
                    TrackCount = 1,
                    PrimaryGenreName = "Hörspiele"
                },
                new ITunesCollectionDto
                {
                    WrapperType = "collection",
                    CollectionId = 9001,
                    CollectionName = "Detektiv Clipper – Folge 7",
                    ArtistId = DetektivClipperArtistId,
                    ArtistName = "Detektiv Clipper",
                    TrackCount = 1,
                    PrimaryGenreName = "Hörspiele"
                }
            ]);

            _ = services.AddSingleton<IAppleMusicSearchClient>(client);
            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes =
                await episodeImport.GetEpisodesAsync(ScotlandYardArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Genau zwei Episoden – das fremde Detektiv-Clipper-Album darf nicht durchrutschen.
            Assert.Equal(2, episodes.Count);
            Assert.All(episodes, e => Assert.Contains("Scotland Yard", e.Title, StringComparison.Ordinal));
            Assert.DoesNotContain(episodes, e => e.Title.Contains("Detektiv Clipper", StringComparison.Ordinal));
        }

        /// <summary>
        /// Delta-Abgleich: Für bereits bekannte Folgen (Titel = Albumname) muss der teure Track-Lookup
        /// entfallen – aber ihre Metadaten (inkl. Cover) müssen weiterhin zurückkommen, damit der
        /// Delta-Import ein fehlendes Cover einer bestehenden Folge nachtragen kann. Nur für die
        /// unbekannte, neue Folge darf ein Track-Lookup ausgelöst werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_KnownTitles_SkipsTrackLookupButKeepsMetadata()
        {
            // ARRANGE
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            const long ArtistId = 555000;
            const long KnownCollectionId = 4001;
            const long NewCollectionId = 4002;

            FakeAppleMusicSearchClient client = new(artists: []);
            client.AddAlbums(ArtistId,
            [
                new ITunesCollectionDto
                {
                    WrapperType = "collection",
                    CollectionId = KnownCollectionId,
                    CollectionName = "Folge 1",
                    ArtistId = ArtistId,
                    ArtistName = "TKKG",
                    ReleaseDate = "2020-01-01T00:00:00Z",
                    ArtworkUrl100 = "https://example.org/100x100/folge1.jpg",
                    TrackCount = 1,
                    PrimaryGenreName = "Hörspiele"
                },
                new ITunesCollectionDto
                {
                    WrapperType = "collection",
                    CollectionId = NewCollectionId,
                    CollectionName = "Folge 2",
                    ArtistId = ArtistId,
                    ArtistName = "TKKG",
                    ReleaseDate = "2021-01-01T00:00:00Z",
                    ArtworkUrl100 = "https://example.org/100x100/folge2.jpg",
                    TrackCount = 1,
                    PrimaryGenreName = "Hörspiele"
                }
            ]);

            _ = services.AddSingleton<IAppleMusicSearchClient>(client);
            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            HashSet<string> known = new(StringComparer.OrdinalIgnoreCase) { "Folge 1" };

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync(
                ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                known,
                TestContext.Current.CancellationToken);

            // ASSERT
            // Beide Alben kommen zurück – die bekannte "Folge 1" wird nicht verschluckt (Cover-Nachtrag).
            Assert.Equal(2, episodes.Count);
            Assert.Contains(episodes, e => e.Title == "Folge 1");
            Assert.Contains(episodes, e => e.Title == "Folge 2");
            // Bekannte "Folge 1" behält ihre Cover-URL aus den Album-Metadaten (kein Track-Lookup nötig).
            Assert.Contains(episodes, e => e.Title == "Folge 1" && !string.IsNullOrEmpty(e.CoverImageUrl));

            // Der teure Track-Lookup lief NUR für die neue, unbekannte Folge – nicht für "Folge 1".
            Assert.Equal([NewCollectionId], client.LookedUpCollectionIds);
        }

        /// <summary>
        /// Stellt sicher, dass eine ungültige SourceSeriesId zu einer ArgumentException führt.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_InvalidSourceSeriesId_ThrowsArgumentException()
        {
            // ARRANGE
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(artists: []));

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT & ASSERT
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => episodeImport.GetEpisodesAsync("keine-gueltige-id", cancellationToken: TestContext.Current.CancellationToken));
        }
    }
}
