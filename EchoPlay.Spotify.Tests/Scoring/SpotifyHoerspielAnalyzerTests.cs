using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Scoring;
using EchoPlay.Spotify.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace EchoPlay.Spotify.Tests.Scoring
{
    /// <summary>
    /// Tests für den Spotify-Hörspiel-Analyzer.
    /// Jeder Test deckt ein einzelnes Analyse-Flag ab.
    /// </summary>
    public sealed class SpotifyHoerspielAnalyzerTests
    {
        /// <summary>
        /// Ein Künstler mit einem negativen Musik-Genre wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NegativeMusicGenre_SetsFlag()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-pop",
                Name = "Popband XY",
                Genres = ["pop"]
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Popband XY");

            Assert.True(result.HasNegativeMusicGenre);
        }

        /// <summary>
        /// Ein Künstler mit dem Namen einer bekannten Serie wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_KnownSeries_SetsFlag()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-tkkg",
                Name = "TKKG",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "TKKG");

            Assert.True(result.IsKnownSeries);
        }

        /// <summary>
        /// Wenn der Suchbegriff im Künstlernamen enthalten ist, wird das Flag gesetzt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NameContainsQuery_SetsFlag()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-nc",
                Name = "Meine Serie ABC",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Serie ABC");

            Assert.True(result.NameContainsQuery);
        }

        /// <summary>
        /// Eine Zahlwort-Variante des Suchbegriffs wird im Künstlernamen erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NumberVariantMatch_SetsFlag()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-nv",
                Name = "Die drei Detektive",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Die 3 Detektive");

            Assert.True(result.HasNumberVariantMatch);
        }

        /// <summary>
        /// Ein exaktes Wort-Match des Suchbegriffs im Künstlernamen wird erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_ExactWordMatch_SetsFlag()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-ew",
                Name = "Serie Abenteuer",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Serie Abenteuer");

            Assert.True(result.HasExactWordMatch);
        }

        /// <summary>
        /// Ein Künstler mit Hörspiel-typischen Alben wird als solcher erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_HoerspielAlbumStructure_SetsFlag()
        {
            ConfigurableSpotifyApiClient apiClient = new ConfigurableSpotifyApiClient()
                .WithAlbums("artist-ha", [CreateAlbum("album-1")])
                .WithTracks("album-1", [CreateHoerspielTrack("track-1")]);

            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(apiClient);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-ha",
                Name = "Hörspielserie XY",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Hörspielserie XY");

            Assert.True(result.HasHoerspielAlbumStructure);
            Assert.True(result.HasAlbums);
        }

        /// <summary>
        /// Ein Künstler ohne Alben wird korrekt erkannt.
        /// </summary>
        [Fact]
        public async Task AnalyzeAsync_NoAlbums_SetsFlagFalse()
        {
            SpotifyHoerspielAnalyzer analyzer = CreateAnalyzer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-na",
                Name = "Ohne Alben",
                Genres = []
            };

            SpotifyHoerspielAnalysis result = await analyzer.AnalyzeAsync(artist, "Ohne Alben");

            Assert.False(result.HasAlbums);
            Assert.False(result.HasHoerspielAlbumStructure);
        }

        // ──────────────────────────────────────────────────────────────
        // Hilfsmethoden
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Erstellt einen Analyzer mit Standardeinstellungen.
        /// </summary>
        private static SpotifyHoerspielAnalyzer CreateAnalyzer(ConfigurableSpotifyApiClient apiClient)
        {
            SpotifyHoerspielSettings settings = new();
            IOptions<SpotifyHoerspielSettings> options = Options.Create(settings);
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());
            return new SpotifyHoerspielAnalyzer(apiClient, options, loggerFactory);
        }

        /// <summary>
        /// Erstellt ein Test-Album.
        /// </summary>
        private static SpotifyAlbumDto CreateAlbum(string albumId)
        {
            return new SpotifyAlbumDto
            {
                SpotifyAlbumId = albumId,
                Title = $"Testalbum {albumId}",
                TotalTracks = 1
            };
        }

        /// <summary>
        /// Erstellt einen einzelnen Hörspiel-typischen Track (45 Minuten).
        /// </summary>
        private static SpotifyTrackDto CreateHoerspielTrack(string trackId)
        {
            return new SpotifyTrackDto
            {
                SpotifyTrackId = trackId,
                Title = $"Hörspielfolge {trackId}",
                Duration = TimeSpan.FromMinutes(45),
                TrackNumber = 1
            };
        }
    }
}
