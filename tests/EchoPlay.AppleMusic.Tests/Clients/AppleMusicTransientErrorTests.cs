using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.AppleMusic.Tests.Clients
{
    /// <summary>
    /// Prüft, was passiert, wenn die Gegenstelle mitten in einem Durchlauf abreißt.
    /// </summary>
    /// <remarks>
    /// Der Import läuft über viele Alben. Ein einzelner Netzfehler ist dabei die Regel,
    /// nicht die Ausnahme. Bricht der ganze Durchlauf deswegen ab, bekommt der Anwender
    /// nach einer Minute Wartezeit gar nichts — obwohl die anderen hundert Alben sauber
    /// geladen wurden.
    /// </remarks>
    public sealed class AppleMusicTransientErrorTests
    {
        private const long ArtistId = 4711;

        [Fact]
        public async Task Analyze_WhenTheTracksOfOneAlbumCannotBeLoaded_SkipsThatAlbum()
        {
            ThrowingTrackClient client = new(
            [
                new ITunesCollectionDto { CollectionId = 1, CollectionName = "Folge 1", ArtistId = ArtistId, WrapperType = "collection" },
            ]);

            AppleMusicHoerspielAnalyzer sut = new(
                client,
                Options.Create(new AppleMusicHoerspielSettings()),
                BuildLoggerFactory());

            // Bewusst kein Name aus der Liste bekannter Serien: Für die stünde die
            // Entscheidung schon fest und die Albenprüfung liefe gar nicht erst an.
            AppleMusicHoerspielAnalysis analysis = await sut.AnalyzeAsync(
                new ITunesArtistDto { ArtistId = ArtistId, ArtistName = "Eine neue Hörspielserie" },
                "Eine neue Hörspielserie",
                TestContext.Current.CancellationToken);

            // Das Album zählt weiterhin als vorhanden — nur seine Struktur bleibt
            // unbeurteilt. Ein Netzfehler darf die Bewertung nicht kippen.
            Assert.True(analysis.HasAlbums);
            Assert.False(analysis.HasHoerspielAlbumStructure);
        }

        [Fact]
        public async Task GetEpisodes_WhenABatchOfTracksFails_ImportsTheEpisodesWithoutDurations()
        {
            ThrowingTrackClient client = new(
            [
                new ITunesCollectionDto
                {
                    CollectionId = 1,
                    CollectionName = "TKKG - Folge 001 - Der Fall",
                    ArtistId = ArtistId,
                    ReleaseDate = "2026-01-01T00:00:00Z",
                    WrapperType = "collection",
                },
            ]);

            AppleMusicEpisodeSource sut = new(client, BuildLoggerFactory());

            IReadOnlyList<ImportEpisode> episodes = await sut.GetEpisodesAsync(
                ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken: TestContext.Current.CancellationToken);

            // Ohne Dauer ist eine Folge immer noch eine Folge. Sie ganz wegzulassen wäre
            // Datenverlust für den Anwender.
            ImportEpisode episode = Assert.Single(episodes);
            Assert.Equal(TimeSpan.Zero, episode.Duration);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetEpisodes_WithoutASeriesId_Throws(string seriesId)
        {
            AppleMusicEpisodeSource sut = new(new ThrowingTrackClient([]), BuildLoggerFactory());

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => sut.GetEpisodesAsync(seriesId, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetEpisodes_WithASeriesIdThatIsNoNumber_Throws()
        {
            AppleMusicEpisodeSource sut = new(new ThrowingTrackClient([]), BuildLoggerFactory());

            // Kennungen anderer Anbieter sehen anders aus. Landet eine davon hier, ist das
            // ein Verdrahtungsfehler und kein leeres Ergebnis.
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => sut.GetEpisodesAsync("spotify:album:xyz", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Search_WithoutASearchTerm_Throws(string query)
        {
            AppleMusicSeriesSearch sut = new(
                new ThrowingTrackClient([]),
                new ThrowingScorer(),
                BuildSettings(),
                BuildLoggerFactory());

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => sut.SearchAsync(query, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Search_WhenTheRatingOfOneArtistFails_SkipsThatArtist()
        {
            ArtistReturningClient client = new(
            [
                new ITunesArtistDto { ArtistId = ArtistId, ArtistName = "TKKG" },
            ]);

            AppleMusicSeriesSearch sut = new(client, new ThrowingScorer(), BuildSettings(), BuildLoggerFactory());

            IReadOnlyList<ImportSeries> results = await sut.SearchAsync(
                "TKKG", TestContext.Current.CancellationToken);

            // Die Bewertung ist eine Heuristik über mehrere Quellen. Scheitert sie für einen
            // Treffer, fällt dieser aus der Liste — die übrigen bleiben.
            Assert.Empty(results);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static LoggerFactory BuildLoggerFactory() => new([], new LoggerOptions());

        private static Microsoft.Extensions.Options.IOptions<AppleMusicHoerspielSettings> BuildSettings() =>
            Microsoft.Extensions.Options.Options.Create(new AppleMusicHoerspielSettings());

        /// <summary>Adapter, der Alben liefert, aber jede Track-Abfrage mit einem Netzfehler beantwortet.</summary>
        private sealed class ThrowingTrackClient : IAppleMusicSearchClient
        {
            private readonly List<ITunesCollectionDto> _albums;

            public ThrowingTrackClient(List<ITunesCollectionDto> albums) => _albums = albums;

            public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(
                string query, int limit = 25, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesArtistDto>());

            public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(
                string query, int limit = 25, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesCollectionDto> { Results = _albums });

            public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(
                long artistId, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesCollectionDto> { Results = _albums });

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(
                long collectionId, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesTrackDto>>(
                    new HttpRequestException("Gegenstelle nicht erreichbar"));

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(
                IReadOnlyList<long> collectionIds, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesTrackDto>>(
                    new HttpRequestException("Gegenstelle nicht erreichbar"));
        }

        /// <summary>Adapter, der eine feste Künstlerliste liefert.</summary>
        private sealed class ArtistReturningClient : IAppleMusicSearchClient
        {
            private readonly List<ITunesArtistDto> _artists;

            public ArtistReturningClient(List<ITunesArtistDto> artists) => _artists = artists;

            public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(
                string query, int limit = 25, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesArtistDto> { Results = _artists });

            public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(
                string query, int limit = 25, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>());

            public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(
                long artistId, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>());

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(
                long collectionId, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesTrackDto>());

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(
                IReadOnlyList<long> collectionIds, CancellationToken ct = default)
                => Task.FromResult(new ITunesResponseDto<ITunesTrackDto>());
        }

        /// <summary>Bewerter, der jede Anfrage mit einem Fehler beantwortet.</summary>
        private sealed class ThrowingScorer : IAppleMusicArtistScorer
        {
            public Task<AppleMusicArtistScore> ScoreArtistAsync(
                ITunesArtistDto artist, string searchQuery, CancellationToken cancellationToken = default)
                => Task.FromException<AppleMusicArtistScore>(
                    new InvalidOperationException("Bewertung nicht möglich"));
        }
    }
}
