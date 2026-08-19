using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Dtos;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Spotify.Tests.Clients
{
    /// <summary>
    /// Prüft, was der Spotify-Adapter aus einer Antwort herausliest: Alben, Alben eines
    /// Künstlers über mehrere Seiten und die Spuren eines Albums.
    /// </summary>
    /// <remarks>
    /// Der Adapter liest die Antwort Feld für Feld statt sie auf ein Objekt abzubilden.
    /// Das ist Absicht — Spotify liefert je nach Album unterschiedlich vollständige
    /// Datensätze. Ein fehlendes Feld darf deshalb den ganzen Import nicht kippen; der
    /// betroffene Eintrag fällt heraus, die übrigen bleiben.
    ///
    /// Die Antworten stehen als Zeichenkette im Test; kein Test geht ins Netz.
    /// </remarks>
    public sealed class SpotifyApiClientReadingTests
    {
        [Fact]
        public async Task SearchAlbums_ReadsTitleArtistCoverAndTrackCount()
        {
            const string antwort = """
            {
              "albums": {
                "items": [
                  {
                    "id": "alb1",
                    "name": "TKKG - Folge 250",
                    "total_tracks": 12,
                    "artists": [ { "name": "TKKG" } ],
                    "images": [ { "url": "https://i.example.invalid/large.jpg" } ]
                  }
                ]
              }
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .SearchAlbumsAsync("TKKG", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

            SpotifyAlbumDto album = Assert.Single(alben);
            Assert.Equal("alb1", album.SpotifyAlbumId);
            Assert.Equal("TKKG - Folge 250", album.Title);
            Assert.Equal(12, album.TotalTracks);
            Assert.Equal("TKKG", album.ArtistName);
            Assert.Equal("https://i.example.invalid/large.jpg", album.ImageUrl);
        }

        [Fact]
        public async Task SearchAlbums_ForAnEntryWithoutIdOrName_LeavesItOut()
        {
            const string antwort = """
            {
              "albums": {
                "items": [
                  { "name": "ohne Kennung", "total_tracks": 1, "artists": [], "images": [] },
                  { "id": "alb2", "total_tracks": 1, "artists": [], "images": [] },
                  { "id": "alb3", "name": "vollständig", "total_tracks": 1, "artists": [], "images": [] }
                ]
              }
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .SearchAlbumsAsync("TKKG", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

            // Ein Eintrag ohne Kennung lässt sich später nicht mehr abrufen. Er hilft
            // niemandem in der Trefferliste.
            SpotifyAlbumDto album = Assert.Single(alben);
            Assert.Equal("alb3", album.SpotifyAlbumId);
        }

        [Fact]
        public async Task SearchAlbums_WithoutASearchTerm_Throws()
        {
            SpotifyApiClient client = BuildClient("""{"albums":{"items":[]}}""");

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => client.SearchAlbumsAsync("   ", limit: 10, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SearchAlbums_WhenTheAnswerIsNotJson_Throws()
        {
            SpotifyApiClient client = BuildClient("<html>Wartung</html>");

            _ = await Assert.ThrowsAnyAsync<JsonException>(
                () => client.SearchAlbumsAsync("TKKG", limit: 10, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetArtistAlbums_ReadsReleaseDateAndTrackCount()
        {
            const string antwort = """
            {
              "items": [
                {
                  "id": "alb1",
                  "name": "TKKG - Folge 001",
                  "release_date": "1981-05-01",
                  "total_tracks": 9,
                  "images": [ { "url": "https://i.example.invalid/1.jpg" } ]
                }
              ],
              "next": null
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .GetArtistAlbumsAsync("art1", limit: 50, cancellationToken: TestContext.Current.CancellationToken);

            SpotifyAlbumDto album = Assert.Single(alben);
            Assert.Equal(new DateTime(1981, 5, 1), album.ReleaseDate);
            Assert.Equal(9, album.TotalTracks);
        }

        [Fact]
        public async Task GetArtistAlbums_WithAnUnreadableReleaseDate_KeepsTheAlbum()
        {
            const string antwort = """
            {
              "items": [
                { "id": "alb1", "name": "Folge 1", "release_date": "irgendwann", "total_tracks": 9, "images": [] }
              ],
              "next": null
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .GetArtistAlbumsAsync("art1", limit: 50, cancellationToken: TestContext.Current.CancellationToken);

            // Spotify gibt das Datum mal als Jahr, mal als Monat, mal als Tag an. Ein
            // unlesbares Datum kostet die Angabe, nicht die Folge.
            SpotifyAlbumDto album = Assert.Single(alben);
            Assert.Null(album.ReleaseDate);
        }

        [Fact]
        public async Task GetArtistAlbums_ForAnEntryWithoutIdOrName_LeavesItOut()
        {
            const string antwort = """
            {
              "items": [
                { "name": "ohne Kennung", "release_date": "2026-01-01", "total_tracks": 1, "images": [] },
                { "id": "alb2", "name": "vollständig", "release_date": "2026-01-01", "total_tracks": 1, "images": [] }
              ],
              "next": null
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .GetArtistAlbumsAsync("art1", limit: 50, cancellationToken: TestContext.Current.CancellationToken);

            SpotifyAlbumDto album = Assert.Single(alben);
            Assert.Equal("alb2", album.SpotifyAlbumId);
        }

        [Fact]
        public async Task GetArtistAlbums_WithSeveralPages_FollowsThemAll()
        {
            SequenceHandler handler = new(
            [
                """
                {
                  "items": [ { "id": "a1", "name": "Folge 1", "release_date": "2026-01-01", "total_tracks": 1, "images": [] } ],
                  "next": "https://api.spotify.invalid/v1/artists/art1/albums?offset=1"
                }
                """,
                """
                {
                  "items": [ { "id": "a2", "name": "Folge 2", "release_date": "2026-02-01", "total_tracks": 1, "images": [] } ],
                  "next": null
                }
                """,
            ]);

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(handler)
                .GetArtistAlbumsAsync("art1", limit: 50, cancellationToken: TestContext.Current.CancellationToken);

            // Spotify liefert höchstens fünfzig Alben je Seite. „Die drei ???" hat über
            // zweihundert Folgen — ohne das Weiterblättern fehlten drei Viertel davon.
            Assert.Equal(2, alben.Count);
            Assert.Equal(2, handler.CallCount);
        }

        [Fact]
        public async Task GetArtistAlbums_WithALimit_StopsAtIt()
        {
            const string antwort = """
            {
              "items": [
                { "id": "a1", "name": "Folge 1", "release_date": "2026-01-01", "total_tracks": 1, "images": [] },
                { "id": "a2", "name": "Folge 2", "release_date": "2026-01-01", "total_tracks": 1, "images": [] },
                { "id": "a3", "name": "Folge 3", "release_date": "2026-01-01", "total_tracks": 1, "images": [] }
              ],
              "next": null
            }
            """;

            IReadOnlyList<SpotifyAlbumDto> alben = await BuildClient(antwort)
                .GetArtistAlbumsAsync("art1", limit: 2, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, alben.Count);
        }

        [Fact]
        public async Task GetArtistAlbums_WithoutAnArtistId_Throws()
        {
            SpotifyApiClient client = BuildClient("""{"items":[],"next":null}""");

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => client.GetArtistAlbumsAsync("  ", limit: 10, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetArtistAlbums_WhenTheProviderIsUnreachable_Throws()
        {
            SpotifyApiClient client = BuildClient(string.Empty, HttpStatusCode.BadGateway);

            _ = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetArtistAlbumsAsync("art1", limit: 10, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetArtistAlbums_WhenTheAnswerIsNotJson_Throws()
        {
            SpotifyApiClient client = BuildClient("<html>Wartung</html>");

            _ = await Assert.ThrowsAnyAsync<JsonException>(
                () => client.GetArtistAlbumsAsync("art1", limit: 10, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetAlbumTracks_ReadsTitleNumberAndDuration()
        {
            const string antwort = """
            {
              "items": [
                { "id": "t1", "name": "Teil 1", "duration_ms": 125000, "track_number": 1 },
                { "id": "t2", "name": "Teil 2", "duration_ms": 60000, "track_number": 2 }
              ]
            }
            """;

            IReadOnlyList<SpotifyTrackDto> spuren = await BuildClient(antwort)
                .GetAlbumTracksAsync("alb1", TestContext.Current.CancellationToken);

            Assert.Equal(2, spuren.Count);
            Assert.Equal("Teil 1", spuren[0].Title);
            Assert.Equal(TimeSpan.FromSeconds(125), spuren[0].Duration);
            Assert.Equal(2, spuren[1].TrackNumber);
        }

        [Fact]
        public async Task GetAlbumTracks_ForATrackWithoutIdOrName_LeavesItOut()
        {
            const string antwort = """
            {
              "items": [
                { "name": "ohne Kennung", "duration_ms": 1000, "track_number": 1 },
                { "id": "t2", "name": "vollständig", "duration_ms": 1000, "track_number": 2 }
              ]
            }
            """;

            IReadOnlyList<SpotifyTrackDto> spuren = await BuildClient(antwort)
                .GetAlbumTracksAsync("alb1", TestContext.Current.CancellationToken);

            // Ohne Kennung lässt sich die Spur später nicht abspielen.
            SpotifyTrackDto spur = Assert.Single(spuren);
            Assert.Equal("t2", spur.SpotifyTrackId);
        }

        [Fact]
        public async Task GetAlbumTracks_WithoutAnAlbumId_Throws()
        {
            SpotifyApiClient client = BuildClient("""{"items":[]}""");

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => client.GetAlbumTracksAsync(" ", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetAlbumTracks_WhenTheProviderIsUnreachable_Throws()
        {
            SpotifyApiClient client = BuildClient(string.Empty, HttpStatusCode.ServiceUnavailable);

            _ = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAlbumTracksAsync("alb1", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetAlbumTracks_WhenTheAnswerIsNotJson_Throws()
        {
            SpotifyApiClient client = BuildClient("kein json");

            _ = await Assert.ThrowsAnyAsync<JsonException>(
                () => client.GetAlbumTracksAsync("alb1", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SearchArtists_ForAnEntryWithoutIdOrName_LeavesItOut()
        {
            const string antwort = """
            {
              "artists": {
                "items": [
                  { "name": "ohne Kennung", "genres": [], "images": [] },
                  { "id": "art2", "name": "TKKG", "genres": ["hörspiel"], "images": [] }
                ]
              }
            }
            """;

            IReadOnlyList<SpotifyArtistDto> artists = await BuildClient(antwort)
                .SearchArtistsAsync("TKKG", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

            SpotifyArtistDto einzig = Assert.Single(artists);
            Assert.Equal("art2", einzig.SpotifyArtistId);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static SpotifyApiClient BuildClient(string antwort, HttpStatusCode status = HttpStatusCode.OK)
            => BuildClient(new SequenceHandler([antwort], status));

        private static SpotifyApiClient BuildClient(SequenceHandler handler)
        {
            HttpClient http = new(handler) { BaseAddress = new Uri("https://api.spotify.invalid/v1/") };
            return new SpotifyApiClient(http, new LoggerFactory([], new LoggerOptions()));
        }

        /// <summary>Antwortgeber, der die hinterlegten Antworten der Reihe nach ausgibt.</summary>
        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly IReadOnlyList<string> _antworten;
            private readonly HttpStatusCode _status;

            public SequenceHandler(IReadOnlyList<string> antworten, HttpStatusCode status = HttpStatusCode.OK)
            {
                _antworten = antworten;
                _status = status;
            }

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string antwort = _antworten[Math.Min(CallCount, _antworten.Count - 1)];
                CallCount++;

                HttpResponseMessage response = new(_status);
                if (!string.IsNullOrEmpty(antwort))
                {
                    response.Content = new StringContent(antwort, Encoding.UTF8, "application/json");
                }

                return Task.FromResult(response);
            }
        }
    }
}
