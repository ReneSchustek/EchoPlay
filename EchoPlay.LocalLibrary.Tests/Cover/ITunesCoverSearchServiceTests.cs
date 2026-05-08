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
    /// Tests für <see cref="ITunesCoverSearchService"/> mit Fake-<see cref="HttpMessageHandler"/>.
    /// </summary>
    public sealed class ITunesCoverSearchServiceTests
    {
        // iTunes liefert artworkUrl100 mit "100x100bb" als Größen-Token; der Service tauscht das aus.
        private const string HappyPathJson = """
            {
              "results": [
                {
                  "artworkUrl100": "https://is1.mzstatic.com/image/cover/100x100bb.jpg",
                  "collectionName": "TKKG – Folge 1"
                }
              ]
            }
            """;

        private const string EmptyJson = """{ "results": [] }""";

        [Fact]
        public async Task SearchAsync_AlbumWithArtwork_ReturnsSingleResult_AndUpscalesUrls()
        {
            ITunesCoverSearchService service = BuildService(HttpStatusCode.OK, HappyPathJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("TKKG");

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal("TKKG – Folge 1", only.ReleaseTitle);
            Assert.Equal("iTunes", only.Source);
            Assert.Contains("250x250bb", only.ThumbnailUrl, StringComparison.Ordinal);
            Assert.Contains("600x600bb", only.FullUrl, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SearchAsync_NoResults_ReturnsEmptyList()
        {
            ITunesCoverSearchService service = BuildService(HttpStatusCode.OK, EmptyJson);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Unbekannt");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_HttpError_ReturnsEmptyList()
        {
            FakeHttpMessageHandler handler = new();
            HttpClient client = new(handler);
            ITunesCoverSearchService service = new(client);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Egal");

            Assert.Empty(result);
        }

        private static ITunesCoverSearchService BuildService(HttpStatusCode status, string body)
        {
            FakeHttpMessageHandler handler = new(status, body);
            HttpClient client = new(handler);
            return new ITunesCoverSearchService(client);
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
