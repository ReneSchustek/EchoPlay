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
    /// Sonderfall zu einer leeren Liste machen — der Dialog soll offen bleiben, damit der
    /// Anwender einen anderen Begriff versuchen kann.
    ///
    /// Alle Antworten stehen als Zeichenkette im Test; kein Test geht ins Netz.
    /// </remarks>
    public sealed class CoverSearchEdgeTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Search_WithoutASearchTerm_ReturnsNothing(string titel)
        {
            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, """{"results":[]}""");

            IReadOnlyList<CoverSearchResult> treffer = await sut.SearchAsync(
                titel, ct: TestContext.Current.CancellationToken);

            Assert.Empty(treffer);
        }

        [Fact]
        public async Task Search_WhenTheAnswerIsLiterallyNull_ReturnsNothing()
        {
            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, "null");

            IReadOnlyList<CoverSearchResult> treffer = await sut.SearchAsync(
                "TKKG", ct: TestContext.Current.CancellationToken);

            Assert.Empty(treffer);
        }

        [Fact]
        public async Task Search_ForAnAlbumWithoutArtwork_LeavesItOut()
        {
            const string antwort = """
                {
                  "results": [
                    { "collectionName": "Ohne Bild" },
                    { "artworkUrl100": "https://is1.example.invalid/100x100bb.jpg", "collectionName": "Mit Bild" }
                  ]
                }
                """;

            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, antwort);

            IReadOnlyList<CoverSearchResult> treffer = await sut.SearchAsync(
                "TKKG", ct: TestContext.Current.CancellationToken);

            // Ein Treffer ohne Bild ist in einem Bilder-Dialog eine leere Kachel.
            CoverSearchResult einzig = Assert.Single(treffer);
            Assert.Equal("Mit Bild", einzig.ReleaseTitle);
        }

        [Fact]
        public async Task Search_OnTheSecondPage_SkipsWhatWasAlreadyShown()
        {
            const string antwort = """
                {
                  "results": [
                    { "artworkUrl100": "https://is1.example.invalid/a/100x100bb.jpg", "collectionName": "Erster" },
                    { "artworkUrl100": "https://is1.example.invalid/b/100x100bb.jpg", "collectionName": "Zweiter" }
                  ]
                }
                """;

            ITunesCoverSearchService sut = BuildITunes(HttpStatusCode.OK, antwort);

            IReadOnlyList<CoverSearchResult> ersteSeite = await sut.SearchAsync(
                "TKKG", CoverSearchPage.First, TestContext.Current.CancellationToken);
            IReadOnlyList<CoverSearchResult> zweiteSeite = await sut.SearchAsync(
                "TKKG", CoverSearchPage.First.Next, TestContext.Current.CancellationToken);

            // Anbieter ohne Versatz liefern beim Nachladen dieselben Treffer erneut. Ohne
            // das Überspringen stünde jede Kachel zweimal im Dialog.
            Assert.True(zweiteSeite.Count < ersteSeite.Count);
        }

        [Fact]
        public void SearchResult_ComparesByItsFourValues()
        {
            CoverSearchResult einer = new("t1", "f1", "Folge 1", "iTunes");
            CoverSearchResult gleich = new("t1", "f1", "Folge 1", "iTunes");
            CoverSearchResult anders = new("t1", "f1", "Folge 1", "Deezer");

            // Zwei Anbieter können dasselbe Bild kennen. Ohne die Herkunft im Vergleich
            // verschwände einer der beiden Treffer aus dem Dialog.
            Assert.Equal(einer, gleich);
            Assert.Equal(einer.GetHashCode(), gleich.GetHashCode());
            Assert.NotEqual(einer, anders);
        }

        [Fact]
        public void SearchResult_IsNeverEqualToNothing()
        {
            CoverSearchResult einer = new("t1", "f1", "Folge 1", "iTunes");

            CoverSearchResult? nichts = null;
            Assert.NotEqual(einer, nichts);
            Assert.False(einer.Equals((object)"kein Treffer"));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static ITunesCoverSearchService BuildITunes(HttpStatusCode status, string body)
        {
            HttpClient http = new(new StubHandler(status, body));
            return new ITunesCoverSearchService(http);
        }

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
