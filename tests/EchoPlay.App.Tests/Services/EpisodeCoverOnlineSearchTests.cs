using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeCoverOnlineSearch"/>: Reihenfolge der Suchbegriffe, Relevanzschwelle
    /// und die Fälle, in denen gar nicht gesucht wird.
    /// </summary>
    public sealed class EpisodeCoverOnlineSearchTests
    {
        private static readonly Uri HitUrl = new("https://example.test/tkkg-12.jpg");

        [Fact]
        public void SearchQueries_NumberAndTitle_GoFromExactToBroad()
        {
            List<string> queries = EpisodeCoverOnlineSearch.SearchQueries("TKKG", "Das Geheimnis", 12);

            Assert.Equal(
                ["TKKG Episode 12 Das Geheimnis", "TKKG 12 Das Geheimnis", "TKKG Das Geheimnis", "TKKG 12"],
                queries);
        }

        [Fact]
        public void SearchQueries_OnlyTitle_SearchesByTitle()
        {
            Assert.Equal(["TKKG Das Geheimnis"], EpisodeCoverOnlineSearch.SearchQueries("TKKG", "Das Geheimnis", null));
        }

        [Fact]
        public void SearchQueries_OnlyNumber_SearchesByNumber()
        {
            Assert.Equal(["TKKG 12"], EpisodeCoverOnlineSearch.SearchQueries("TKKG", " ", 12));
        }

        [Fact]
        public void SearchQueries_NeitherNumberNorTitle_IsEmpty()
        {
            Assert.Empty(EpisodeCoverOnlineSearch.SearchQueries("TKKG", string.Empty, null));
        }

        [Fact]
        public async Task FindCoverAsync_RelevantHit_DownloadsIt()
        {
            FakeCoverSearchService search = new();
            search.SetResults([new CoverSearchResult(HitUrl, HitUrl, "TKKG 12 Das Geheimnis", "Test")]);
            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [HitUrl.OriginalString] = [1, 2, 3] });

            byte[]? cover = await Build(search, downloader).FindCoverAsync("TKKG", "TKKG - 12 - Das Geheimnis", 12, TestContext.Current.CancellationToken);

            Assert.Equal([1, 2, 3], cover);
            // Seriennamen und Nummer schneidet die Suche vom Titel ab, bevor sie fragt.
            Assert.Equal("TKKG Episode 12 Das Geheimnis", search.LastSearchTitle);
        }

        [Fact]
        public async Task FindCoverAsync_OnlyIrrelevantHits_DownloadsNothing()
        {
            FakeCoverSearchService search = new();
            search.SetResults([new CoverSearchResult(HitUrl, HitUrl, "Mimi Rutherford", "Test")]);
            FakeCoverDownloader downloader = new();

            byte[]? cover = await Build(search, downloader).FindCoverAsync("TKKG", "Das Geheimnis", 12, TestContext.Current.CancellationToken);

            Assert.Null(cover);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task FindCoverAsync_WithoutSeriesName_DoesNotSearch()
        {
            FakeCoverSearchService search = new();

            byte[]? cover = await Build(search, new FakeCoverDownloader()).FindCoverAsync(" ", "Das Geheimnis", 12, TestContext.Current.CancellationToken);

            Assert.Null(cover);
            Assert.Null(search.LastSearchTitle);
        }

        [Fact]
        public async Task FindCoverAsync_SearchFails_TriesTheNextQuery()
        {
            // Die erste Anfrage scheitert, die zweite trifft: Ein Fehler kostet nur diese Formulierung.
            ThrowingOnceCoverSearch search = new(new CoverSearchResult(HitUrl, HitUrl, "TKKG 12 Das Geheimnis", "Test"));
            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [HitUrl.OriginalString] = [9] });

            byte[]? cover = await Build(search, downloader).FindCoverAsync("TKKG", "Das Geheimnis", 12, TestContext.Current.CancellationToken);

            Assert.Equal([9], cover);
            Assert.Equal(2, search.Calls);
        }

        [Fact]
        public async Task FindCoverAsync_CallerCancels_Throws()
        {
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();
            ThrowingOnceCoverSearch search = new(null, cancelWith: cts.Token);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Build(search, new FakeCoverDownloader()).FindCoverAsync("TKKG", "Das Geheimnis", 12, cts.Token));
        }

        [Fact]
        public async Task FindCoverAsync_NoSearchServiceRegistered_ReturnsNull()
        {
            ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            EpisodeCoverOnlineSearch onlineSearch = new(
                provider.GetRequiredService<IServiceScopeFactory>(), new FakeCoverDownloader(), new CapturingLogger());

            Assert.Null(await onlineSearch.FindCoverAsync("TKKG", "Das Geheimnis", 12, TestContext.Current.CancellationToken));
        }

        private static EpisodeCoverOnlineSearch Build(ICoverSearchService search, FakeCoverDownloader downloader)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<ICoverSearchService>(search);
            ServiceProvider provider = services.BuildServiceProvider();
            return new EpisodeCoverOnlineSearch(provider.GetRequiredService<IServiceScopeFactory>(), downloader, new CapturingLogger());
        }

        /// <summary>
        /// Scheitert beim ersten Aufruf, danach liefert es den Treffer; mit Abbruchzeichen wirft es
        /// stattdessen den Abbruch.
        /// </summary>
        private sealed class ThrowingOnceCoverSearch(CoverSearchResult? hit, CancellationToken cancelWith = default) : ICoverSearchService
        {
            public int Calls { get; private set; }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
            {
                Calls++;
                cancelWith.ThrowIfCancellationRequested();
                if (Calls == 1)
                {
                    throw new System.Net.Http.HttpRequestException("Gegenstelle nicht erreichbar");
                }

                return Task.FromResult<IReadOnlyList<CoverSearchResult>>(hit is null ? [] : [hit]);
            }
        }
    }
}
