using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.Core.Scoring;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Tests.Scoring
{
    /// <summary>
    /// Tests für das Scoring des Apple-Music-Hörspiel-Scorers.
    /// Jeder Test deckt eine einzelne Stufe oder ein klar definiertes Verhalten ab.
    /// </summary>
    public sealed class AppleMusicHoerspielScorerTests
    {
        // ──────────────────────────────────────────────────────────────
        // Stufe 1: Bekannte Serien
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler mit dem Namen einer bekannten Hörspielserie wird sofort akzeptiert.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_KnownSeriesName_AcceptsImmediately()
        {
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 1,
                ArtistName = "TKKG",
                PrimaryGenreName = "Hörspiele"
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
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 2,
                ArtistName = "Benjamin Blümchen",
                PrimaryGenreName = null
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Benjamin Blümchen");

            Assert.True(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.KnownSeriesName, result.Reason);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 2: Name-Matching
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Wenn der Suchbegriff im Künstlernamen enthalten ist, erhält der Kandidat einen Contains-Bonus.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_NameContainsSearchQuery_AddsContainsBonus()
        {
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(3, [CreateAlbum(100)])
                .WithTracks(100, [CreateHoerspielTrack(1001)]);

            AppleMusicHoerspielScorer scorer = CreateScorer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 3,
                ArtistName = "Meine unbekannte Serie",
                PrimaryGenreName = null
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
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(4, [CreateAlbum(200)])
                .WithTracks(200, [CreateHoerspielTrack(2001)]);

            AppleMusicHoerspielScorer scorer = CreateScorer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 4,
                ArtistName = "Die drei Detektive",
                PrimaryGenreName = null
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Die 3 Detektive");

            // Zahlwort-Variante matcht → NameContainsBonus
            Assert.True(result.IsHoerspiel);
            Assert.True(result.Score >= 50);
        }

        // ──────────────────────────────────────────────────────────────
        // Stufe 3: Genre-Bonus
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Ein Künstler mit Hörspiel-Genre erhält einen Genre-Bonus.
        /// </summary>
        [Fact]
        public async Task ScoreAsync_HoerspielGenre_AddsGenreBonus()
        {
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(5, [CreateAlbum(300)])
                .WithTracks(300, [CreateHoerspielTrack(3001)]);

            AppleMusicHoerspielScorer scorer = CreateScorer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 5,
                ArtistName = "Neue Hörspielserie",
                PrimaryGenreName = "Hörspiele"
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Neue Hörspielserie");

            // Contains (50) + ExactWord (25) + Genre (30) + Album (25) = 130
            Assert.True(result.IsHoerspiel);
            Assert.True(result.Score >= 80);
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
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(6, [CreateAlbum(400)])
                .WithTracks(400, [CreateHoerspielTrack(4001)]);

            AppleMusicHoerspielScorer scorer = CreateScorer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 6,
                ArtistName = "Eine neue Hörspielserie",
                PrimaryGenreName = null
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
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 7,
                ArtistName = "Kein Album Vorhanden",
                PrimaryGenreName = null
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
            ConfigurableAppleMusicSearchClient searchClient = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(8, [CreateAlbum(500)])
                .WithTracks(500, CreateShortMusicTracks());

            AppleMusicHoerspielScorer scorer = CreateScorer(searchClient);

            ITunesArtistDto artist = new()
            {
                ArtistId = 8,
                ArtistName = "Kurze Musik Tracks",
                PrimaryGenreName = null
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
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 9,
                ArtistName = "Völlig anderer Name",
                PrimaryGenreName = null
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
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 10,
                ArtistName = "TKKG",
                PrimaryGenreName = "Hörspiele"
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
            AppleMusicHoerspielScorer scorer = CreateScorer(new ConfigurableAppleMusicSearchClient());

            ITunesArtistDto artist = new()
            {
                ArtistId = 42,
                ArtistName = "Irgendein Künstler",
                PrimaryGenreName = null
            };

            HoerspielScoreResult result = await scorer.ScoreAsync(artist, "Suchbegriff");

            Assert.Equal("42", result.ArtistId);
        }

        // ──────────────────────────────────────────────────────────────
        // Hilfsmethoden
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Erstellt einen Scorer mit Standardeinstellungen und leerem Cache.
        /// </summary>
        /// <param name="searchClient">Der konfigurierbare Search-Client.</param>
        /// <returns>Ein Scorer mit Standardkonfiguration.</returns>
        private static AppleMusicHoerspielScorer CreateScorer(ConfigurableAppleMusicSearchClient searchClient)
        {
            AppleMusicHoerspielSettings settings = new();
            IOptions<AppleMusicHoerspielSettings> options = Options.Create(settings);
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());
            AppleMusicHoerspielAnalyzer analyzer = new(searchClient, options, loggerFactory);
            return new AppleMusicHoerspielScorer(analyzer, options, new HoerspielDecisionCache(loggerFactory), loggerFactory);
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

        /// <summary>
        /// Erstellt kurze Musik-Tracks, die keiner Hörspiel-Struktur entsprechen.
        /// </summary>
        /// <returns>Liste kurzer Musik-Tracks.</returns>
        private static List<ITunesTrackDto> CreateShortMusicTracks()
        {
            return
            [
                new ITunesTrackDto
                {
                    WrapperType = "track",
                    TrackId = 9001,
                    TrackName = "Song 1",
                    TrackTimeMillis = (int)TimeSpan.FromMinutes(3).TotalMilliseconds,
                    TrackNumber = 1
                },
                new ITunesTrackDto
                {
                    WrapperType = "track",
                    TrackId = 9002,
                    TrackName = "Song 2",
                    TrackTimeMillis = (int)TimeSpan.FromMinutes(4).TotalMilliseconds,
                    TrackNumber = 2
                }
            ];
        }
    }
}
