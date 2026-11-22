using EchoPlay.LocalLibrary.Tests.Fakes;
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
    /// Prüft die Randfälle der Cover-Suche: kein Suchbegriff, eine Antwort ohne Inhalt,
    /// Treffer ohne hinterlegtes Bild und das Weiterblättern bei Anbietern ohne Versatz.
    /// </summary>
    /// <remarks>
    /// Die Suche läuft über vier fremde Gegenstellen mit vier verschiedenen Vorstellungen
    /// davon, wie eine Antwort aussieht. Der gemeinsame Unterbau muss deshalb jeden
    /// Sonderfall zu einer leeren Liste machen, der Dialog soll offen bleiben, damit der
    /// Anwender einen anderen Begriff versuchen kann.
    ///
    /// Alle Antworten stehen als Zeichenkette im Test; kein Test geht ins Netz.
    /// </remarks>
    public sealed class CoverSearchEdgeTests
    {
        private static readonly Uri Thumb = new("https://example.invalid/t1.jpg");
        private static readonly Uri Full = new("https://example.invalid/f1.jpg");

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Search_WithoutASearchTerm_ReturnsNothing(string title)
        {
            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, """{"results":[]}""");

            IReadOnlyList<CoverSearchResult> hit = await sut.SearchAsync(
                title, ct: TestContext.Current.CancellationToken);

            Assert.Empty(hit);
        }

        [Fact]
        public async Task Search_WhenTheAnswerIsLiterallyNull_ReturnsNothing()
        {
            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, "null");

            IReadOnlyList<CoverSearchResult> hit = await sut.SearchAsync(
                "TKKG", ct: TestContext.Current.CancellationToken);

            Assert.Empty(hit);
        }

        [Fact]
        public async Task Search_ForAnAlbumWithoutArtwork_LeavesItOut()
        {
            const string response = """
                {
                  "results": [
                    { "collectionName": "Ohne Bild" },
                    { "artworkUrl100": "https://is1.example.invalid/100x100bb.jpg", "collectionName": "Mit Bild" }
                  ]
                }
                """;

            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, response);

            IReadOnlyList<CoverSearchResult> hit = await sut.SearchAsync(
                "TKKG", ct: TestContext.Current.CancellationToken);

            // Ein Treffer ohne Bild ist in einem Bilder-Dialog eine leere Kachel.
            CoverSearchResult einzig = Assert.Single(hit);
            Assert.Equal("Mit Bild", einzig.ReleaseTitle);
        }

        [Fact]
        public async Task Search_OnTheSecondPage_SkipsWhatWasAlreadyShown()
        {
            const string response = """
                {
                  "results": [
                    { "artworkUrl100": "https://is1.example.invalid/a/100x100bb.jpg", "collectionName": "Erster" },
                    { "artworkUrl100": "https://is1.example.invalid/b/100x100bb.jpg", "collectionName": "Zweiter" }
                  ]
                }
                """;

            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, response);

            IReadOnlyList<CoverSearchResult> firstPage = await sut.SearchAsync(
                "TKKG", CoverSearchPage.First, TestContext.Current.CancellationToken);
            IReadOnlyList<CoverSearchResult> secondPage = await sut.SearchAsync(
                "TKKG", CoverSearchPage.First.Next, TestContext.Current.CancellationToken);

            // Anbieter ohne Versatz liefern beim Nachladen dieselben Treffer erneut. Ohne
            // das Überspringen stünde jede Kachel zweimal im Dialog.
            Assert.True(secondPage.Count < firstPage.Count);
        }

        [Fact]
        public void SearchResult_ComparesByItsFourValues()
        {
            CoverSearchResult one = new(Thumb, Full, "Folge 1", "iTunes");
            CoverSearchResult same = new(Thumb, Full, "Folge 1", "iTunes");
            CoverSearchResult anders = new(Thumb, Full, "Folge 1", "Deezer");

            // Zwei Anbieter können dasselbe Bild kennen. Ohne die Herkunft im Vergleich
            // verschwände einer der beiden Treffer aus dem Dialog.
            Assert.Equal(one, same);
            Assert.Equal(one.GetHashCode(), same.GetHashCode());
            Assert.NotEqual(one, anders);
        }

        [Fact]
        public void SearchResult_IsNeverEqualToNothing()
        {
            CoverSearchResult one = new(Thumb, Full, "Folge 1", "iTunes");

            CoverSearchResult? nothing = null;
            Assert.NotEqual(one, nothing);
            Assert.False(one.Equals((object)"kein Treffer"));
        }

        private static ITunesCoverSearchService BuildITunes(HttpStatusCode status, string body)
        {
            HttpClient http = new(new StubHandler(status, body));
            return new ITunesCoverSearchService(http, new FakeLoggerFactory());
        }

        /// <summary>
        /// Antwortet mit festem Status und festem Text.
        /// </summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
