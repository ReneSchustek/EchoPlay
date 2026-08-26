using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Tests.Search
{
    /// <summary>
    /// Tests des Suchlaufs: was er einspart, in welcher Reihenfolge er meldet und wie er
    /// sich verhält, wenn der Nutzer weitertippt.
    /// </summary>
    /// <remarks>
    /// Der Anlass war eine Suche nach „Bibi", die zweieinhalb Minuten lief und dabei keinen
    /// einzigen Treffer zeigte. Jeder Test hier hält eine der Ursachen fest.
    /// </remarks>
    public sealed class AppleMusicSeriesSearchStreamTests
    {
        [Fact]
        public async Task Search_ForAKnownSeries_DoesNotAskForAlbums()
        {
            // „Bibi Blocksberg" steht in der Liste bekannter Serien. Die Bewertung nimmt sie
            // hart an — die vier Anfragen der Albenprüfung wären für die Tonne.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists([Artist(1, "Bibi Blocksberg", "Hörspiele")]);

            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(client);

            AppleMusicHoerspielAnalysis analysis = await analyzer.AnalyzeAsync(
                Artist(1, "Bibi Blocksberg", "Hörspiele"), "Bibi", TestContext.Current.CancellationToken);

            Assert.True(analysis.IsKnownSeries);
            Assert.Equal(0, client.LookupAlbumsCalls);
            Assert.Equal(0, client.LookupTracksBatchCalls);
        }

        [Fact]
        public async Task Search_ForAnUnknownArtist_AsksForAllTracksInOneRequest()
        {
            // Drei Alben, aber nur eine Titelabfrage: Die Lookup-API nimmt mehrere Kennungen
            // entgegen, und jede eingesparte Anfrage kostet an der Ratenbremse 1,5 Sekunden.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(9, [Album(100), Album(101), Album(102)])
                .WithTracks(100, [HoerspielTrack(1001)])
                .WithTracks(101, [HoerspielTrack(1002)])
                .WithTracks(102, [HoerspielTrack(1003)]);

            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(client);

            AppleMusicHoerspielAnalysis analysis = await analyzer.AnalyzeAsync(
                Artist(9, "Eine neue Hörspielserie", null), "Eine neue Hörspielserie", TestContext.Current.CancellationToken);

            Assert.True(analysis.HasHoerspielAlbumStructure);
            Assert.Equal(1, client.LookupTracksBatchCalls);
            Assert.Equal(0, client.LookupTracksCalls);
        }

        [Fact]
        public async Task Search_ForAnUnknownArtist_TakesTheCoverFromTheFirstAlbum()
        {
            // Auf Künstlerebene liefert die Suchantwort kein Artwork. Das erste Album hat eines,
            // und seine Liste liegt für die Albenprüfung ohnehin schon vor.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithAlbums(9, [Album(100, "https://is1-ssl.mzstatic.com/image/thumb/abc/100x100bb.jpg")])
                .WithTracks(100, [HoerspielTrack(1001)]);

            AppleMusicHoerspielAnalyzer analyzer = CreateAnalyzer(client);

            AppleMusicHoerspielAnalysis analysis = await analyzer.AnalyzeAsync(
                Artist(9, "Eine neue Hörspielserie", null), "Eine neue Hörspielserie", TestContext.Current.CancellationToken);

            Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/abc/300x300bb.jpg", analysis.ArtworkUrl);
        }

        [Fact]
        public async Task Search_WithMoreCandidatesThanAllowed_LimitsTheExpensiveChecks()
        {
            // Sechs namentlich passende Künstler, aber nur vier dürfen die Albenprüfung
            // auslösen. Bekannte Serien zählen nicht mit — sie kosten keine Anfrage.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists(
                [
                    Artist(1, "Bibi Blocksberg", "Hörspiele"),
                    Artist(2, "Bibi und Tina", "Hörspiele"),
                    Artist(3, "Bibi Eins", null),
                    Artist(4, "Bibi Zwei", null),
                    Artist(5, "Bibi Drei", null),
                    Artist(6, "Bibi Vier", null),
                    Artist(7, "Bibi Fünf", null),
                    Artist(8, "Bibi Sechs", null),
                ]);

            FakeAppleMusicHoerspielScorer scorer = new(
                HoerspielScoreResult.Yes("1", HoerspielDecisionReason.None, 60, "Test"));

            AppleMusicSeriesSearch sut = new(client, scorer, Settings(), LoggerFactory());

            IReadOnlyList<ImportSeries> results = await sut.SearchAsync("Bibi", TestContext.Current.CancellationToken);

            // Zwei bekannte Serien plus vier geprüfte Kandidaten.
            Assert.Equal(6, results.Count);
            Assert.Equal(6, scorer.ScoredArtists.Count);
        }

        [Fact]
        public async Task Search_WhenTheUserKeepsTyping_StopsWithoutAWarning()
        {
            // Vorher lief die Schleife nach dem Abbruch weiter und schrieb für jeden
            // verbliebenen Künstler eine Warnung — 14 Stück innerhalb von 32 Millisekunden,
            // die keinen einzigen Fehler beschrieben.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists(
                [
                    Artist(1, "Bibi Eins", "Hörspiele"),
                    Artist(2, "Bibi Zwei", "Hörspiele"),
                    Artist(3, "Bibi Drei", "Hörspiele"),
                ]);

            CapturingLogSink sink = new();
            AppleMusicSeriesSearch sut = new(client, new CancellingScorer(), Settings(), new LoggerFactory([sink], new LoggerOptions()));

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await sut.SearchAsync("Bibi", TestContext.Current.CancellationToken));

            Assert.Empty(sink.Warnings);
        }

        [Fact]
        public async Task Search_WithAnAlreadyCancelledToken_StopsBeforeTheFirstRating()
        {
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists([Artist(1, "Bibi Blocksberg", "Hörspiele")]);

            FakeAppleMusicHoerspielScorer scorer = new(
                HoerspielScoreResult.Yes("1", HoerspielDecisionReason.KnownSeriesName, 100, "Test"));

            AppleMusicSeriesSearch sut = new(client, scorer, Settings(), LoggerFactory());

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await sut.SearchAsync("Bibi", cts.Token));

            Assert.Empty(scorer.ScoredArtists);
        }

        [Fact]
        public async Task SearchStream_ReportsEachHitAsSoonAsItIsDecided()
        {
            // Der Strom darf nicht erst am Ende etwas herausgeben: Genau daran lag es, dass
            // die Seite minutenlang leer blieb.
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists(
                [
                    Artist(1, "Bibi Blocksberg", "Hörspiele"),
                    Artist(2, "Bibi und Tina", "Hörspiele"),
                ]);

            FakeAppleMusicHoerspielScorer scorer = new(
                HoerspielScoreResult.Yes("1", HoerspielDecisionReason.KnownSeriesName, 100, "Test"));

            AppleMusicSeriesSearch sut = new(client, scorer, Settings(), LoggerFactory());

            await using IAsyncEnumerator<ImportSeries> stream =
                sut.SearchStreamAsync("Bibi", TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

            Assert.True(await stream.MoveNextAsync());

            // Nach dem ersten Treffer ist erst ein Künstler bewertet — der zweite läuft noch.
            _ = Assert.Single(scorer.ScoredArtists);
        }

        [Fact]
        public async Task Search_ForAKnownSeries_PassesTheCoverToTheResult()
        {
            ConfigurableAppleMusicSearchClient client = new ConfigurableAppleMusicSearchClient()
                .WithArtists([Artist(1, "Bibi Blocksberg", "Hörspiele")]);

            FakeAppleMusicHoerspielScorer scorer = new(
                HoerspielScoreResult.Yes("1", HoerspielDecisionReason.KnownSeriesName, 100, "Test"),
                artworkUrl: "https://is1-ssl.mzstatic.com/image/thumb/abc/300x300bb.jpg");

            AppleMusicSeriesSearch sut = new(client, scorer, Settings(), LoggerFactory());

            IReadOnlyList<ImportSeries> results = await sut.SearchAsync("Bibi", TestContext.Current.CancellationToken);

            ImportSeries series = Assert.Single(results);
            Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/abc/300x300bb.jpg", series.CoverImageUrl);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static LoggerFactory LoggerFactory() => new([], new LoggerOptions());

        private static IOptions<AppleMusicHoerspielSettings> Settings() =>
            Options.Create(new AppleMusicHoerspielSettings());

        private static AppleMusicHoerspielAnalyzer CreateAnalyzer(ConfigurableAppleMusicSearchClient client) =>
            new(client, Settings(), LoggerFactory());

        private static ITunesArtistDto Artist(long id, string name, string? genre) =>
            new() { ArtistId = id, ArtistName = name, PrimaryGenreName = genre };

        private static ITunesCollectionDto Album(long collectionId, string? artworkUrl = null) =>
            new()
            {
                CollectionId = collectionId,
                CollectionName = $"Folge {collectionId}",
                WrapperType = "collection",
                ArtworkUrl100 = artworkUrl
            };

        private static ITunesTrackDto HoerspielTrack(long trackId) =>
            new()
            {
                WrapperType = "track",
                TrackId = trackId,
                TrackName = $"Titel {trackId}",
                // Eine Viertelstunde je Titel — das ist die Länge, an der die Heuristik ein
                // Hörspiel von einem Musikstück unterscheidet.
                TrackTimeMillis = 900_000
            };

        /// <summary>Bewerter, der den Abbruch der umgebenden Operation nachstellt.</summary>
        private sealed class CancellingScorer : IAppleMusicArtistScorer
        {
            public Task<AppleMusicArtistScore> ScoreArtistAsync(
                ITunesArtistDto artist, string searchQuery, CancellationToken cancellationToken = default)
                => Task.FromException<AppleMusicArtistScore>(new OperationCanceledException());
        }
    }
}
