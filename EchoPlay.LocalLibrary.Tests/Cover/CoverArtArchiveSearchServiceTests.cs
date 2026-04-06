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
    /// Tests für <see cref="CoverArtArchiveSearchService"/>.
    /// Verwendet einen Fake-<see cref="HttpMessageHandler"/>, damit kein echter Netzwerkzugriff stattfindet.
    /// </summary>
    public sealed class CoverArtArchiveSearchServiceTests
    {
        // Minimale MusicBrainz-Antwort mit einem Release, das eine MBID hat
        private const string SingleReleaseJson = """
            {
              "releases": [
                {
                  "id": "abc-123",
                  "title": "TKKG – Hörspiel"
                }
              ]
            }
            """;

        // Leere MusicBrainz-Antwort ohne Treffer
        private const string EmptyReleasesJson = """
            {
              "releases": []
            }
            """;

        /// <summary>
        /// Erstellt einen <see cref="CoverArtArchiveSearchService"/> mit einem kontrollierten
        /// <see cref="HttpMessageHandler"/>, der alle Requests mit den übergebenen Status-Codes
        /// und dem optionalen JSON-Body beantwortet.
        /// </summary>
        private static CoverArtArchiveSearchService BuildService(
            HttpStatusCode musicBrainzStatus,
            string? musicBrainzBody,
            HttpStatusCode coverArtStatus)
        {
            FakeHttpMessageHandler handler = new(musicBrainzStatus, musicBrainzBody, coverArtStatus);
            HttpClient client = new(handler);
            return new CoverArtArchiveSearchService(client);
        }

        [Fact]
        public async Task SearchAsync_EmptyTitle_ReturnsEmptyList()
        {
            // Leerer Suchbegriff darf keinen HTTP-Request auslösen und muss sofort leer zurückgeben
            CoverArtArchiveSearchService service = BuildService(
                HttpStatusCode.OK, SingleReleaseJson, HttpStatusCode.OK);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync(string.Empty);

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_NoReleasesFound_ReturnsEmptyList()
        {
            // Wenn MusicBrainz keine Releases findet, muss eine leere Liste zurückgegeben werden
            CoverArtArchiveSearchService service = BuildService(
                HttpStatusCode.OK, EmptyReleasesJson, HttpStatusCode.OK);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("Unbekannte Serie");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_ReleaseWithCover_ReturnsSingleResult()
        {
            // Wenn MusicBrainz einen Release mit MBID findet und das Cover Art Archive antwortet,
            // muss genau ein Ergebnis zurückgegeben werden
            CoverArtArchiveSearchService service = BuildService(
                HttpStatusCode.OK, SingleReleaseJson, HttpStatusCode.OK);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("TKKG");

            Assert.Single(result);
            Assert.Equal("TKKG – Hörspiel", result[0].ReleaseTitle);
            Assert.Equal("Cover Art Archive", result[0].Source);
            // URL enthält die MBID aus dem JSON
            Assert.Contains("abc-123", result[0].ThumbnailUrl);
            Assert.Contains("abc-123", result[0].FullUrl);
        }

        [Fact]
        public async Task SearchAsync_ReleaseWithoutCover_ReturnsEmptyList()
        {
            // Wenn das Cover Art Archive 404 zurückgibt, darf der Release nicht im Ergebnis erscheinen
            CoverArtArchiveSearchService service = BuildService(
                HttpStatusCode.OK, SingleReleaseJson, HttpStatusCode.NotFound);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("TKKG");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_NetworkError_ReturnsEmptyList()
        {
            // Ein Netzwerkfehler darf nicht zu einer Exception führen – leere Liste als Fallback
            FakeHttpMessageHandler handler = new(throwException: true);
            HttpClient client = new(handler);
            CoverArtArchiveSearchService service = new(client);

            IReadOnlyList<CoverSearchResult> result = await service.SearchAsync("TKKG");

            Assert.Empty(result);
        }

        // ── Fake HTTP-Handler ──────────────────────────────────────────────────

        /// <summary>
        /// Steuerbarer <see cref="HttpMessageHandler"/> für Tests.
        /// Unterscheidet MusicBrainz-Anfragen (GET mit JSON-Body) von
        /// Cover Art Archive-HEAD-Requests (HEAD-Request).
        /// </summary>
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _musicBrainzStatus;
            private readonly string? _musicBrainzBody;
            private readonly HttpStatusCode _coverArtStatus;
            private readonly bool _throwException;

            /// <summary>Initialisiert den Handler mit normalen Antworten.</summary>
            public FakeHttpMessageHandler(
                HttpStatusCode musicBrainzStatus,
                string? musicBrainzBody,
                HttpStatusCode coverArtStatus)
            {
                _musicBrainzStatus = musicBrainzStatus;
                _musicBrainzBody   = musicBrainzBody;
                _coverArtStatus    = coverArtStatus;
                _throwException    = false;
            }

            /// <summary>Initialisiert den Handler so, dass er immer eine Exception wirft.</summary>
            public FakeHttpMessageHandler(bool throwException)
            {
                _throwException = throwException;
            }

            /// <inheritdoc/>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (_throwException)
                {
                    throw new HttpRequestException("Simulierter Netzwerkfehler");
                }

                // HEAD-Request → Cover Art Archive-Prüfung
                if (request.Method == HttpMethod.Head)
                {
                    return Task.FromResult(new HttpResponseMessage(_coverArtStatus));
                }

                // GET-Request → MusicBrainz-Suche
                HttpResponseMessage response = new(_musicBrainzStatus);

                if (_musicBrainzBody is not null)
                {
                    response.Content = new StringContent(
                        _musicBrainzBody,
                        Encoding.UTF8,
                        "application/json");
                }

                return Task.FromResult(response);
            }
        }
    }
}
