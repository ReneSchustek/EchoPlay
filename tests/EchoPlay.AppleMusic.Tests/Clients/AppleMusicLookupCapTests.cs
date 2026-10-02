using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;

namespace EchoPlay.AppleMusic.Tests.Clients
{
    /// <summary>
    /// Sichert den Umgang mit dem Deckel der iTunes-Lookup-Antwort ab: Eine Anfrage trägt
    /// höchstens 200 Alben, und die Standardreihenfolge lässt ausgerechnet die neuesten Folgen
    /// weg. Lange Serien brauchen deshalb die zweite Anfrage mit <c>sort=recent</c>.
    /// </summary>
    public sealed class AppleMusicLookupCapTests
    {
        private const int Cap = 200;

        [Fact]
        public async Task LookupAlbumsAsync_UnterDemDeckel_FragtNurEinmal()
        {
            SequenceHandler handler = new([BuildResponse(1, 10)]);
            AppleMusicSearchClient client = BuildClient(handler);

            ITunesResponseDto<ITunesCollectionDto> result =
                await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken);

            _ = Assert.Single(handler.RequestUris);
            Assert.Equal(10, CountAlbums(result));
        }

        [Fact]
        public async Task LookupAlbumsAsync_DeckelAusgeschoepft_FragtNeuesteNach()
        {
            SequenceHandler handler = new([BuildResponse(1, Cap), BuildResponse(Cap + 1, 5)]);
            AppleMusicSearchClient client = BuildClient(handler);

            _ = await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken);

            Assert.Equal(2, handler.RequestUris.Count);
            Assert.DoesNotContain("sort=recent", handler.RequestUris[0].Query, StringComparison.Ordinal);
            Assert.Contains("sort=recent", handler.RequestUris[1].Query, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LookupAlbumsAsync_DeckelAusgeschoepft_LegtBeideAntwortenZusammen()
        {
            // Die zweite Antwort überschneidet sich bewusst mit der ersten: iTunes liefert in
            // beiden Reihenfolgen dieselben Alben, nur unterschiedlich beschnitten.
            SequenceHandler handler = new([BuildResponse(1, Cap), BuildResponse(Cap - 4, 10)]);
            AppleMusicSearchClient client = BuildClient(handler);

            ITunesResponseDto<ITunesCollectionDto> result =
                await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken);

            // Die zweite Antwort führt 196 bis 205; nur 201 bis 205 sind neu.
            Assert.Equal(Cap + 5, CountAlbums(result));
            Assert.Equal(CountAlbums(result), AlbumIds(result).Distinct().Count());
            Assert.Contains(Cap + 5L, AlbumIds(result));
        }

        [Fact]
        public async Task LookupAlbumsAsync_DeckelAusgeschoepft_BehaeltDenKuenstlerEintragEinmal()
        {
            SequenceHandler handler = new([BuildResponse(1, Cap), BuildResponse(Cap + 1, 3)]);
            AppleMusicSearchClient client = BuildClient(handler);

            ITunesResponseDto<ITunesCollectionDto> result =
                await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken);

            int artistEntries = result.Results.Count(
                r => string.Equals(r.WrapperType, "artist", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, artistEntries);
        }

        // ── Test-Helfer ──────────────────────────────────────────────────────────

        /// <summary>Baut eine Lookup-Antwort mit Künstler-Eintrag und fortlaufenden Alben.</summary>
        /// <param name="firstId">Collection-ID des ersten Albums.</param>
        /// <param name="count">Anzahl der Alben.</param>
        private static string BuildResponse(int firstId, int count)
        {
            StringBuilder json = new();
            _ = json.Append(CultureInfo.InvariantCulture, $$"""{"resultCount":{{count + 1}},"results":[{"wrapperType":"artist","artistId":100}""");

            for (int i = 0; i < count; i++)
            {
                int id = firstId + i;
                _ = json.Append(CultureInfo.InvariantCulture,
                    $$""",{"wrapperType":"collection","collectionId":{{id}},"collectionName":"Folge {{id}}","artistId":100}""");
            }

            _ = json.Append("]}");
            return json.ToString();
        }

        private static int CountAlbums(ITunesResponseDto<ITunesCollectionDto> response) =>
            response.Results.Count(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<long> AlbumIds(ITunesResponseDto<ITunesCollectionDto> response) =>
            response.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.CollectionId);

        private static AppleMusicSearchClient BuildClient(SequenceHandler handler)
        {
            HttpClient http = new(handler) { BaseAddress = new Uri("https://itunes.apple.com/") };
            return new AppleMusicSearchClient(http, new LoggerFactory([], new LoggerOptions()));
        }

        /// <summary>Antwortet der Reihe nach; die letzte Antwort wiederholt sich.</summary>
        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly IReadOnlyList<string> _responses;
            private int _calls;

            public List<Uri> RequestUris { get; } = [];

            public SequenceHandler(IReadOnlyList<string> responses) => _responses = responses;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (request.RequestUri is not null)
                {
                    RequestUris.Add(request.RequestUri);
                }

                string json = _responses[Math.Min(_calls, _responses.Count - 1)];
                _calls++;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
