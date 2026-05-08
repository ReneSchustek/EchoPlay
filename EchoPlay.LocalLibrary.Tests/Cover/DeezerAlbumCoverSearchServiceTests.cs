using EchoPlay.LocalLibrary.Cover;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Tests für <see cref="DeezerAlbumCoverSearchService"/> mit Fake-<see cref="HttpMessageHandler"/>.
    /// </summary>
    public sealed class DeezerAlbumCoverSearchServiceTests
    {
        private const string HappyPathJson = """
            {
              "data": [
                {
                  "title": "Folge 1 – Der Fluch",
                  "cover_medium": "https://e-cdns-images.dzcdn.net/medium.jpg",
                  "cover_xl": "https://e-cdns-images.dzcdn.net/xl.jpg"
                }
              ]
            }
            """;

        private const string EmptyJson = """{ "data": [] }""";

        [Fact]
        public async Task SearchAsync_AlbumWithCover_ReturnsSingleResult()
        {
            DeezerAlbumCoverSearchService service = BuildService(HttpStatusCode.OK, HappyPathJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("TKKG");

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal("Folge 1 – Der Fluch", only.ReleaseTitle);
            Assert.Equal("Deezer (Album)", only.Source);
            Assert.Equal("https://e-cdns-images.dzcdn.net/medium.jpg", only.ThumbnailUrl);
            Assert.Equal("https://e-cdns-images.dzcdn.net/xl.jpg", only.FullUrl);
        }

        [Fact]
        public async Task SearchAsync_NoData_ReturnsEmptyList()
        {
            DeezerAlbumCoverSearchService service = BuildService(HttpStatusCode.OK, EmptyJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Unbekannt");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_HttpError_ReturnsEmptyList()
        {
            FakeHttpMessageHandler handler = new();
            HttpClient client = new(handler);
            DeezerAlbumCoverSearchService service = new(client);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Egal");

            Assert.Empty(result);
        }

        private static DeezerAlbumCoverSearchService BuildService(HttpStatusCode status, string body)
        {
            FakeHttpMessageHandler handler = new(status, body);
            HttpClient client = new(handler);
            return new DeezerAlbumCoverSearchService(client);
        }

        // Lokaler Fake-Handler: GET liefert konfigurierten Status + Body, Throw-Modus simuliert Netzwerkfehler.
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string? _body;
            private readonly bool _throw;

            public FakeHttpMessageHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public FakeHttpMessageHandler()
            {
                _throw = true;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_throw)
                {
                    throw new HttpRequestException("Simulierter Netzwerkfehler");
                }

                HttpResponseMessage response = new(_status);
                if (_body is not null)
                {
                    response.Content = new StringContent(_body, Encoding.UTF8, "application/json");
                }
                return Task.FromResult(response);
            }
        }
    }
}
