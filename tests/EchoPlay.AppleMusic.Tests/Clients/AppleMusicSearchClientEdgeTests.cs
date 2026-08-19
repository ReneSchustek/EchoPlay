using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.AppleMusic.Tests.Clients
{
    /// <summary>
    /// Prüft die Randfälle des iTunes-Adapters: leere Eingaben, Sammelabfragen und
    /// Antworten, die keine sind.
    /// </summary>
    /// <remarks>
    /// Der Adapter steht zwischen der Anwendung und einer fremden Gegenstelle, auf deren
    /// Verhalten niemand Einfluss hat. Eine Antwort, die sich nicht lesen lässt, darf nicht
    /// als leeres Ergebnis durchgehen — sonst sähe der Anwender „keine Treffer", wo in
    /// Wahrheit die Gegenstelle kaputt ist.
    /// </remarks>
    public sealed class AppleMusicSearchClientEdgeTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SearchArtists_WithoutASearchTerm_Throws(string query)
        {
            AppleMusicSearchClient client = BuildClient("""{"resultCount":0,"results":[]}""");

            // Eine Anfrage ohne Suchbegriff liefert bei iTunes die halbe Welt. Sie gehört
            // gar nicht erst abgeschickt.
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => client.SearchArtistsAsync(query, ct: TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SearchAlbums_WithoutASearchTerm_Throws(string query)
        {
            AppleMusicSearchClient client = BuildClient("""{"resultCount":0,"results":[]}""");

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => client.SearchAlbumsAsync(query, ct: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task LookupTracks_ForOneAlbum_AsksForItsSongs()
        {
            RecordingHandler handler = new(HttpStatusCode.OK, """{"resultCount":0,"results":[]}""");
            AppleMusicSearchClient client = BuildClient(handler);

            _ = await client.LookupTracksAsync(collectionId: 4711, ct: TestContext.Current.CancellationToken);

            Uri asked = Assert.Single(handler.RequestUris);
            Assert.Contains("id=4711", asked.Query, StringComparison.Ordinal);
            Assert.Contains("entity=song", asked.Query, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LookupTracksBatch_WithoutAList_Throws()
        {
            AppleMusicSearchClient client = BuildClient("""{"resultCount":0,"results":[]}""");

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => client.LookupTracksBatchAsync(null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task LookupTracksBatch_WithAnEmptyList_AsksNothing()
        {
            RecordingHandler handler = new(HttpStatusCode.OK, """{"resultCount":0,"results":[]}""");
            AppleMusicSearchClient client = BuildClient(handler);

            ITunesResponseDto<ITunesTrackDto> result = await client.LookupTracksBatchAsync(
                [], TestContext.Current.CancellationToken);

            // Ohne Alben gibt es nichts zu holen. Eine Anfrage ohne Kennungen liefert bei
            // iTunes einen Fehler — die spart sich der Adapter.
            Assert.Empty(result.Results);
            Assert.Empty(handler.RequestUris);
        }

        [Fact]
        public async Task LookupTracksBatch_WithSeveralAlbums_AsksForThemInOneRequest()
        {
            RecordingHandler handler = new(HttpStatusCode.OK, """{"resultCount":0,"results":[]}""");
            AppleMusicSearchClient client = BuildClient(handler);

            _ = await client.LookupTracksBatchAsync([11, 22, 33], TestContext.Current.CancellationToken);

            // Eine Serie hat schnell zweihundert Folgen. Einzelne Anfragen dafür rennen in
            // jede Begrenzung der Gegenstelle.
            Uri asked = Assert.Single(handler.RequestUris);
            Assert.Contains("id=11,22,33", Uri.UnescapeDataString(asked.Query), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Lookup_WhenTheAnswerIsLiterallyNull_Throws()
        {
            AppleMusicSearchClient client = BuildClient("null");

            // „null" ist eine gültige JSON-Antwort, aber kein leeres Ergebnis. Ginge sie als
            // solches durch, meldete die Anwendung „keine Folgen" statt eines Fehlers.
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.LookupAlbumsAsync(artistId: 1, ct: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Lookup_WhenTheAnswerIsNotJson_Throws()
        {
            AppleMusicSearchClient client = BuildClient("<html>Wartungsarbeiten</html>");

            // Gegenstellen antworten im Störungsfall gern mit einer HTML-Seite.
            _ = await Assert.ThrowsAnyAsync<JsonException>(
                () => client.LookupAlbumsAsync(artistId: 1, ct: TestContext.Current.CancellationToken));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static AppleMusicSearchClient BuildClient(string responseJson)
            => BuildClient(new RecordingHandler(HttpStatusCode.OK, responseJson));

        private static AppleMusicSearchClient BuildClient(RecordingHandler handler)
        {
            HttpClient http = new(handler) { BaseAddress = new Uri("https://itunes.apple.invalid/") };
            return new AppleMusicSearchClient(http, new LoggerFactory([], new LoggerOptions()));
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _responseJson;

            public RecordingHandler(HttpStatusCode statusCode, string responseJson)
            {
                _statusCode = statusCode;
                _responseJson = responseJson;
            }

            public List<Uri> RequestUris { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (request.RequestUri is not null)
                {
                    RequestUris.Add(request.RequestUri);
                }

                HttpResponseMessage response = new(_statusCode);
                if (!string.IsNullOrEmpty(_responseJson))
                {
                    response.Content = new StringContent(_responseJson, Encoding.UTF8, "application/json");
                }

                return Task.FromResult(response);
            }
        }
    }
}
