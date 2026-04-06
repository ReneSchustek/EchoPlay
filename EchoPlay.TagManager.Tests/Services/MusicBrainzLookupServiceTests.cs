using System.Net;
using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;

namespace EchoPlay.TagManager.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="MusicBrainzLookupService"/>.
    /// Alle Tests verwenden <see cref="FakeHttpMessageHandler"/>, damit keine echten
    /// Netzwerkanfragen an MusicBrainz gesendet werden.
    /// </summary>
    public sealed class MusicBrainzLookupServiceTests
    {
        private static MusicBrainzLookupService CreateService(string jsonResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            FakeHttpMessageHandler handler = new(jsonResponse, statusCode);
            HttpClient httpClient = new(handler)
            {
                BaseAddress = new Uri("https://musicbrainz.org/")
            };
            return new MusicBrainzLookupService(httpClient, new FakeLoggerFactory());
        }

        [Fact]
        public async Task SearchAsync_ReturnsMappedResults()
        {
            // Minimale MusicBrainz-Suchantwort mit einem Release
            const string json = """
                {
                    "releases": [
                        {
                            "title": "TKKG 200",
                            "date": "2023-05-01",
                            "track-count": 1,
                            "artist-credit": [
                                {
                                    "artist": { "name": "TKKG" }
                                }
                            ]
                        }
                    ]
                }
                """;

            MusicBrainzLookupService service = CreateService(json);

            IReadOnlyList<TagLookupResult> results = await service.SearchAsync("TKKG 200");

            Assert.Single(results);
            Assert.Equal("TKKG 200", results[0].Title);
            Assert.Equal("TKKG", results[0].Artist);
            Assert.Equal(2023u, results[0].Year);
            Assert.Equal(1u, results[0].TrackCount);
            Assert.Equal("MusicBrainz", results[0].Source);
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmptyList_WhenNoResults()
        {
            const string json = """{ "releases": [] }""";

            MusicBrainzLookupService service = CreateService(json);

            IReadOnlyList<TagLookupResult> results = await service.SearchAsync("UnbekannterTitel");

            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchAsync_ThrowsOnHttpError()
        {
            // MusicBrainz liefert 503 (Service Unavailable) → HttpRequestException
            MusicBrainzLookupService service = CreateService("{}", HttpStatusCode.ServiceUnavailable);

            await Assert.ThrowsAsync<HttpRequestException>(() => service.SearchAsync("Irgendwas"));
        }
    }
}
