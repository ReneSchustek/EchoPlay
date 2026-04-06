using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Scoring;
using EchoPlay.Spotify.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace EchoPlay.Spotify.Tests.Scoring
{
    /// <summary>
    /// Tests für das 5-Stufen-Scoring des Spotify-Hörspiel-Scorers.
    /// Jeder Test deckt eine einzelne Stufe oder ein klar definiertes Verhalten ab.
    /// </summary>
    public sealed class SpotifyHoerspielScorerTests
    {
        // ──────────────────────────────────────────────────────────────
        // Stufe 1: Musik-Genre-Filter
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler mit einem negativen Musik-Genre wird sofort abgelehnt,
        /// unabhängig von Name oder Album-Struktur.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NegativeMusicGenre_RejectsImmediately()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-pop",
                Name = "Die drei ???",
                Genres = ["pop", "dance"]
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Die drei ???");

            Assert.False(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.NegativeMusicGenre, result.Reason);
            Assert.Equal(0, result.Score);
        }

        /// <summary>
        /// Ein Genre-Teilstring wie "indie rock" wird als negativ erkannt,
        /// weil "rock" in der Negativliste enthalten ist.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_GenreContainsNegativeSubstring_RejectsImmediately()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-indie",
                Name = "Unbekannter Künstler",
                Genres = ["indie rock"]
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Unbekannter Künstler");

            Assert.False(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.NegativeMusicGenre, result.Reason);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 2: Bekannte Serien
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler mit dem Namen einer bekannten Hörspielserie wird sofort akzeptiert.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_KnownSeriesName_AcceptsImmediately()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-tkkg",
                Name = "TKKG",
                Genres = ["hörspiel"]
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "TKKG");

            Assert.True(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.KnownSeriesName, result.Reason);
            Assert.Equal(100, result.Score);
        }

        /// <summary>
        /// Die Erkennung bekannter Serien funktioniert auch mit Umlauten im Namen.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_KnownSeriesWithUmlauts_AcceptsImmediately()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-bb",
                Name = "Benjamin Blümchen",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Benjamin Blümchen");

            Assert.True(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.KnownSeriesName, result.Reason);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 3: Name-Matching
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Wenn der Suchbegriff im Künstlernamen enthalten ist, erhält der Kandidat einen Contains-Bonus.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NameContainsSearchQuery_AddsContainsBonus()
        {
            ConfigurableSpotifyApiClient apiClient = new ConfigurableSpotifyApiClient()
                .WithAlbums("artist-unknown", [CreateAlbum("album-1")])
                .WithTracks("album-1", [CreateHoerspielTrack("track-1")]);

            SpotifyHoerspielScorer scorer = CreateScorer(apiClient);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-unknown",
                Name = "Meine unbekannte Serie",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "unbekannte Serie");

            // Contains-Bonus (50) + ExactWordMatch-Bonus (25) + AlbumStructure-Bonus (25) = 100
            Assert.True(result.IsHoerspiel);
            Assert.True(result.Score >= 50);
        }

        /// <summary>
        /// Zahlwort-Varianten ermöglichen den Match von "Die 3 ???" mit "Die drei ???".
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NumberVariantMatch_AddsBonus()
        {
            ConfigurableSpotifyApiClient apiClient = new ConfigurableSpotifyApiClient()
                .WithAlbums("artist-d3", [CreateAlbum("album-1")])
                .WithTracks("album-1", [CreateHoerspielTrack("track-1")]);

            SpotifyHoerspielScorer scorer = CreateScorer(apiClient);

            // Künstler heißt "Die drei", aber Suche verwendet Ziffer
            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-d3",
                Name = "Die drei Detektive",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Die 3 Detektive");

            // Zahlwort-Variante "Die drei Detektive" matcht → NameContainsBonus
            Assert.True(result.IsHoerspiel);
            Assert.True(result.Score >= 50);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 4: Album-Struktur
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler mit hörspiel-typischen Alben erhält einen Album-Struktur-Bonus.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_HoerspielAlbumStructure_AddsAlbumBonus()
        {
            ConfigurableSpotifyApiClient apiClient = new ConfigurableSpotifyApiClient()
                .WithAlbums("artist-hs", [CreateAlbum("album-1")])
                .WithTracks("album-1", [CreateHoerspielTrack("track-1")]);

            SpotifyHoerspielScorer scorer = CreateScorer(apiClient);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-hs",
                Name = "Eine neue Hörspielserie",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Eine neue Hörspielserie");

            // Contains (50) + ExactWord (25) + Album (25) = 100
            Assert.True(result.IsHoerspiel);
            Assert.True(result.Score >= 75);
        }

        /// <summary>
        /// Ein Künstler ohne Alben erhält einen Abzug.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NoAlbums_AppliesPenalty()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-empty",
                Name = "Kein Album Vorhanden",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Kein Album Vorhanden");

            // Contains (50) + ExactWord (25) + NoAlbumPenalty (-25) = 50 → gerade noch akzeptiert
            Assert.Equal(50, result.Score);
        }

        /// <summary>
        /// Ein Künstler mit Musik-Alben (kurze Tracks) erhält einen Abzug statt eines Bonus.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_MusicAlbumStructure_AppliesPenalty()
        {
            ConfigurableSpotifyApiClient apiClient = new ConfigurableSpotifyApiClient()
                .WithAlbums("artist-musik", [CreateAlbum("album-m")])
                .WithTracks("album-m", CreateShortMusicTracks());

            SpotifyHoerspielScorer scorer = CreateScorer(apiClient);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-musik",
                Name = "Kurze Musik Tracks",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Kurze Musik Tracks");

            // Contains (50) + ExactWord (25) + NoAlbumPenalty (-25) = 50
            Assert.Equal(50, result.Score);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 5: Finale Entscheidung
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler ohne Namensübereinstimmung und ohne Hörspiel-Alben wird abgelehnt.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NoMatchNoAlbums_RejectsWithLowScore()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-nomatch",
                Name = "Völlig anderer Name",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Ganz anderer Suchbegriff");

            Assert.False(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.None, result.Reason);
            Assert.True(result.Score < 50);
        }

        // ──────────────────────────────────────────────────────────────
        // Cache-Integration
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein bereits bewerteter Künstler wird beim zweiten Aufruf aus dem Cache bedient.
        /// Das Ergebnis muss identisch sein.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_SameArtistTwice_ReturnsCachedResult()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-cached",
                Name = "TKKG",
                Genres = []
            };

            HoerspielScoreResult first = await scorer.ScoreAsync(artist, "TKKG");
            HoerspielScoreResult second = await scorer.ScoreAsync(artist, "TKKG");

            // Beide Ergebnisse müssen identisch sein (selbe Referenz aus dem Cache)
            Assert.Same(first, second);
            Assert.True(first.IsHoerspiel);
        }

        // ──────────────────────────────────────────────────────────────
        // ArtistId-Weitergabe
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Die Artist-ID wird korrekt im Ergebnis mitgeführt.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_Always_SetsArtistIdInResult()
        {
            SpotifyHoerspielScorer scorer = CreateScorer(new ConfigurableSpotifyApiClient());

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-id-check",
                Name = "Irgendein Künstler",
                Genres = []
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Suchbegriff");

            Assert.Equal("artist-id-check", result.ArtistId);
        }

        // ──────────────────────────────────────────────────────────────
        // Hilfsmethoden
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Erstellt einen Scorer mit Standardeinstellungen und leerem Cache.
        /// </summary>
        private static SpotifyHoerspielScorer CreateScorer(ConfigurableSpotifyApiClient apiClient)
        {
            SpotifyHoerspielSettings settings = new();
            IOptions<SpotifyHoerspielSettings> options = Options.Create(settings);
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());
            SpotifyHoerspielAnalyzer analyzer = new(apiClient, options, loggerFactory);
            return new SpotifyHoerspielScorer(analyzer, options, new HoerspielDecisionCache(loggerFactory), loggerFactory);
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

        /// <summary>
        /// Erstellt kurze Musik-Tracks, die keiner Hörspiel-Struktur entsprechen.
        /// </summary>
        private static IReadOnlyList<SpotifyTrackDto> CreateShortMusicTracks()
        {
            return
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "music-1",
                    Title = "Song 1",
                    Duration = TimeSpan.FromMinutes(3),
                    TrackNumber = 1
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "music-2",
                    Title = "Song 2",
                    Duration = TimeSpan.FromMinutes(4),
                    TrackNumber = 2
                }
            ];
        }
    }
}
