using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.AppleMusic.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Tests.Scoring
{
    /// <summary>
    /// Tests für den Apple-Music-Hörspiel-Analyzer.
    /// Jeder Test deckt ein einzelnes Analyse-Flag ab.
    /// </summary>
    public sealed class AppleMusicHoerspielAnalyzerTests
    {
        /// <summary>
        /// Ein Künstler mit dem Namen einer bekannten Serie wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_KnownSeries_SetsFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 1,
                ArtistName = "TKKG",
                PrimaryGenreName = "Hörspiele"
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "TKKG");

            Assert.True(result.IsKnownSeries);
        }

        /// <summary>
        /// Wenn der Suchbegriff im Künstlernamen enthalten ist, wird das Flag gesetzt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NameContainsQuery_SetsFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 2,
                ArtistName = "Meine Serie ABC",
                PrimaryGenreName = null
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Serie ABC");

            Assert.True(result.NameContainsQuery);
        }

        /// <summary>
        /// Eine Zahlwort-Variante des Suchbegriffs wird im Künstlernamen erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NumberVariantMatch_SetsFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 3,
                ArtistName = "Die drei Detektive",
                PrimaryGenreName = null
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Die 3 Detektive");

            Assert.True(result.HasNumberVariantMatch);
        }

        /// <summary>
        /// Ein exaktes Wort-Match des Suchbegriffs im Künstlernamen wird erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_ExactWordMatch_SetsFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 4,
                ArtistName = "Serie Abenteuer",
                PrimaryGenreName = null
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Serie Abenteuer");

            Assert.True(result.HasExactWordMatch);
        }

        /// <summary>
        /// Ein Künstler mit Hörspiel-Genre wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_HoerspielGenre_SetsFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 5,
                ArtistName = "Irgendein Künstler",
                PrimaryGenreName = "Hörspiele"
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Irgendein Künstler");

            Assert.True(result.HasHoerspielGenre);
        }

        /// <summary>
        /// Ein Künstler mit einem Nicht-Hörspiel-Genre wird korrekt erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NonHoerspielGenre_DoesNotSetFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 6,
                ArtistName = "Popband XY",
                PrimaryGenreName = "Pop"
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Popband XY");

            Assert.False(result.HasHoerspielGenre);
        }

        /// <summary>
        /// Ein Künstler mit Hörspiel-typischen Alben wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_HoerspielAlbumStructure_SetsFlag()
        {
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(7, [CreateAlbum(100)])
                .WithTracks(100, [CreateHoerspielTrack(1001)]);

            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 7,
                ArtistName = "Hörspielserie XY",
                PrimaryGenreName = null
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Hörspielserie XY");

            Assert.True(result.HasHoerspielAlbumStructure);
            Assert.True(result.HasAlbums);
        }

        /// <summary>
        /// Ein Künstler ohne Alben wird korrekt erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NoAlbums_SetsFlagFalse()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 8,
                ArtistName = "Ohne Alben",
                PrimaryGenreName = null
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Ohne Alben");

            Assert.False(result.HasAlbums);
            Assert.False(result.HasHoerspielAlbumStructure);
        }

        /// <summary>
        /// Das Genre "Kinder und Jugend" wird als Hörspiel-Genre erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_KinderGenre_SetsHoerspielGenreFlag()
        {
            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 9,
                ArtistName = "Kinderserie XY",
                PrimaryGenreName = "Kinder und Jugend"
            };

            AppleMusicHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Kinderserie XY");

            Assert.True(result.HasHoerspielGenre);
        }

        // ──────────────────────────────────────────────────────────────
        // Hilfsmethoden
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Erstellt einen Analyzer mit Standardeinstellungen.
        /// </summary>
        /// <param name="searchClient">Der konfigurierbare Search-Client.</param>
        /// <returns>Ein Analyzer mit Standardkonfiguration.</returns>
        private static AppleMusicHoerspielAnalyzer CreateAnalyzer(ConfigurableAppleMusicSearchClient searchClient)
        {
            AppleMusicHoerspielSettings settings = new();
            IOptions<AppleMusicHoerspielSettings> options = Options.Create(settings);
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());
            return new AppleMusicHoerspielAnalyzer(searchClient, options, loggerFactory);
        }

        /// <summary>
        /// Erstellt ein Test-Album.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID.</param>
        /// <returns>Ein Album-DTO mit WrapperType "collection".</returns>
        private static ITunesCollectionDto CreateAlbum(long collectionId)
        {
            return new ITunesCollectionDto
            {
                WrapperType = "collection",
                CollectionId = collectionId,
                CollectionName = $"Testalbum {collectionId}",
                TrackCount = 1,
                PrimaryGenreName = "Hörspiele"
            };
        }

        /// <summary>
        /// Erstellt einen einzelnen Hörspiel-typischen Track (45 Minuten).
        /// </summary>
        /// <param name="trackId">Die iTunes-Track-ID.</param>
        /// <returns>Ein Track-DTO mit WrapperType "track".</returns>
        private static ITunesTrackDto CreateHoerspielTrack(long trackId)
        {
            return new ITunesTrackDto
            {
                WrapperType = "track",
                TrackId = trackId,
                TrackName = $"Hörspielfolge {trackId}",
                TrackTimeMillis = (int)TimeSpan.FromMinutes(45).TotalMilliseconds,
                TrackNumber = 1
            };
        }
    }
}
