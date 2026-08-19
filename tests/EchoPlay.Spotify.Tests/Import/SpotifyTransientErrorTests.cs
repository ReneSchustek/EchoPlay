using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;
using EchoPlay.Spotify.Scoring;
using EchoPlay.Spotify.Services;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Spotify.Tests.Import
{
    /// <summary>
    /// Prüft, was geschieht, wenn Spotify mitten in einem Durchlauf nicht mehr antwortet.
    /// </summary>
    /// <remarks>
    /// Eine Serie mit zweihundert Folgen bedeutet zweihundert Anfragen. Dass eine davon
    /// scheitert, ist der Normalfall. Bricht der Import deswegen ab, hat der Anwender nach
    /// minutenlangem Warten gar nichts — statt hundertneunundneunzig Folgen.
    /// </remarks>
    public sealed class SpotifyTransientErrorTests
    {
        [Fact]
        public async Task GetEpisodes_WhenTheTracksOfOneAlbumFail_SkipsThatEpisode()
        {
            ThrowingTrackClient client = new(
            [
                new SpotifyAlbumDto { SpotifyAlbumId = "alb1", Title = "Folge 1", TotalTracks = 3 },
            ]);

            SpotifyEpisodeImportSource sut = new(client, BuildLoggerFactory());

            IReadOnlyList<ImportEpisode> folgen = await sut.GetEpisodesAsync(
                "art1", cancellationToken: TestContext.Current.CancellationToken);

            // Ohne Spuren lässt sich die Dauer nicht bestimmen. Die Folge ohne diese
            // Angabe zu importieren wäre eine stille Falschangabe.
            Assert.Empty(folgen);
        }

        [Fact]
        public async Task GetEpisodes_ForAnAlreadyKnownEpisode_AsksNoTracks()
        {
            CountingTrackClient client = new(
            [
                new SpotifyAlbumDto { SpotifyAlbumId = "alb1", Title = "Folge 1", TotalTracks = 3 },
            ]);

            SpotifyEpisodeImportSource sut = new(client, BuildLoggerFactory());

            _ = await sut.GetEpisodesAsync(
                "art1",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Folge 1" },
                TestContext.Current.CancellationToken);

            // Beim Abgleich auf neue Folgen kostet jede bekannte Folge sonst eine
            // zusätzliche Anfrage — bei zweihundert Folgen zweihundert überflüssige.
            Assert.Equal(0, client.TrackCalls);
        }

        [Fact]
        public async Task Analyze_WhenTheTracksOfOneAlbumFail_SkipsThatAlbum()
        {
            ThrowingTrackClient client = new(
            [
                new SpotifyAlbumDto { SpotifyAlbumId = "alb1", Title = "Folge 1", TotalTracks = 3 },
            ]);

            SpotifyHoerspielAnalyzer sut = new(
                client, Options.Create(new SpotifyHoerspielSettings()), BuildLoggerFactory());

            SpotifyHoerspielAnalysis analyse = await sut.AnalyzeAsync(
                new SpotifyArtistDto { SpotifyArtistId = "art1", Name = "TKKG", Genres = [] },
                "TKKG",
                TestContext.Current.CancellationToken);

            // Das Album zählt weiter als vorhanden, nur seine Struktur bleibt unbeurteilt.
            Assert.True(analyse.HasAlbums);
            Assert.False(analyse.HasHoerspielAlbumStructure);
        }

        [Fact]
        public async Task Search_WhenTheRatingOfOneArtistFails_SkipsThatArtist()
        {
            ArtistOnlyClient client = new(
            [
                new SpotifyArtistDto { SpotifyArtistId = "art1", Name = "TKKG", Genres = [] },
            ]);

            SpotifySeriesImportSearch sut = new(
                client, new SpotifySeriesMapper(new ThrowingScorer()), BuildLoggerFactory());

            IReadOnlyList<ImportSeries> treffer = await sut.SearchAsync(
                "TKKG", TestContext.Current.CancellationToken);

            // Die Bewertung ist eine Heuristik über mehrere Merkmale. Scheitert sie für
            // einen Treffer, fällt dieser heraus — die übrigen bleiben in der Liste.
            Assert.Empty(treffer);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static LoggerFactory BuildLoggerFactory() => new([], new LoggerOptions());

        /// <summary>Adapter, der Alben liefert, aber jede Spurabfrage mit einem Netzfehler beantwortet.</summary>
        private sealed class ThrowingTrackClient : ISpotifyApiClient
        {
            private readonly List<SpotifyAlbumDto> _alben;

            public ThrowingTrackClient(List<SpotifyAlbumDto> alben) => _alben = alben;

            public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyArtistDto>>([]);

            public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>(_alben);

            public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(
                string artistId, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>(_alben);

            public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(
                string albumId, CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<SpotifyTrackDto>>(
                    new HttpRequestException("Spotify nicht erreichbar"));
        }

        /// <summary>Adapter, der die Spurabfragen mitzählt.</summary>
        private sealed class CountingTrackClient : ISpotifyApiClient
        {
            private readonly List<SpotifyAlbumDto> _alben;

            public CountingTrackClient(List<SpotifyAlbumDto> alben) => _alben = alben;

            public int TrackCalls { get; private set; }

            public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyArtistDto>>([]);

            public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>(_alben);

            public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(
                string artistId, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>(_alben);

            public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(
                string albumId, CancellationToken cancellationToken = default)
            {
                TrackCalls++;
                return Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
            }
        }

        /// <summary>Adapter, der nur Künstler kennt.</summary>
        private sealed class ArtistOnlyClient : ISpotifyApiClient
        {
            private readonly List<SpotifyArtistDto> _artists;

            public ArtistOnlyClient(List<SpotifyArtistDto> artists) => _artists = artists;

            public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyArtistDto>>(_artists);

            public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(
                string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);

            public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(
                string artistId, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);

            public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(
                string albumId, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
        }

        /// <summary>Bewerter, der jede Anfrage mit einem Fehler beantwortet.</summary>
        private sealed class ThrowingScorer : IHoerspielScorer<SpotifyArtistDto>
        {
            public Task<HoerspielScoreResult> ScoreAsync(
                SpotifyArtistDto source, string searchQuery, CancellationToken cancellationToken = default)
                => Task.FromException<HoerspielScoreResult>(
                    new InvalidOperationException("Bewertung nicht möglich"));
        }
    }
}
