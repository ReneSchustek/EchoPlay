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
    /// Tests für <see cref="DiscogsCoverSearchService"/> mit Fake-<see cref="HttpMessageHandler"/>.
    /// </summary>
    public sealed class DiscogsCoverSearchServiceTests
    {
        private const string HappyPathJson = """
            {
              "results": [
                {
                  "title": "Die drei ??? – Vinyl-Reissue",
                  "thumb": "https://i.discogs.com/thumb.jpg",
                  "cover_image": "https://i.discogs.com/cover.jpg"
                }
              ]
            }
            """;

        private const string EmptyJson = """{ "results": [] }""";

        [Fact]
        public async Task SearchAsync_ReleaseWithCover_ReturnsSingleResult()
        {
            DiscogsCoverSearchService service = BuildService(HttpStatusCode.OK, HappyPathJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Die drei ???");

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal("Die drei ??? – Vinyl-Reissue", only.ReleaseTitle);
            Assert.Equal("Discogs", only.Source);
            Assert.Equal("https://i.discogs.com/thumb.jpg", only.ThumbnailUrl);
            Assert.Equal("https://i.discogs.com/cover.jpg", only.FullUrl);
        }

        [Fact]
        public async Task SearchAsync_NoResults_ReturnsEmptyList()
        {
            DiscogsCoverSearchService service = BuildService(HttpStatusCode.OK, EmptyJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Unbekannt");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_HttpError_ReturnsEmptyList()
        {
            FakeHttpMessageHandler handler = new();
            HttpClient client = new(handler);
            DiscogsCoverSearchService service = new(client);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Egal");

            Assert.Empty(result);
        }

        private static DiscogsCoverSearchService BuildService(HttpStatusCode status, string body)
        {
            FakeHttpMessageHandler handler = new(status, body);
            HttpClient client = new(handler);
            return new DiscogsCoverSearchService(client);
        }

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
