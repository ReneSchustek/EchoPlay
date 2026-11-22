using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, in welcher Reihenfolge <see cref="DashboardSnapshot"/> die Favoriten liefert.
    /// </summary>
    /// <remarks>
    /// „Weiterhören" und die Favoriten-Kacheln übernehmen diese Reihenfolge. Las der Snapshot
    /// sie aus einem anderen Abschnitt als dem, in den das Ziehen und Ablegen schreibt, stand
    /// „Weiterhören" alphabetisch, während die Favoriten der gezogenen Reihenfolge folgten.
    /// </remarks>
    public sealed class DashboardSnapshotTests
    {
        [Fact]
        public async Task LoadAsync_WithoutSavedOrder_SortsFavoritesByTitle()
        {
            FakeSeriesDataService series = new();
            _ = await AddFavoriteAsync(series, "TKKG");
            _ = await AddFavoriteAsync(series, "Bibi Blocksberg");

            DashboardSnapshot snapshot = await LoadAsync(series, new FakeDashboardPositionDataService());

            Assert.Equal(["Bibi Blocksberg", "TKKG"], Titles(snapshot));
        }

        [Fact]
        public async Task LoadAsync_WithSavedFavoriteOrder_FollowsIt()
        {
            FakeSeriesDataService series = new();
            Series first = await AddFavoriteAsync(series, "Bibi Blocksberg");
            Series second = await AddFavoriteAsync(series, "TKKG");

            FakeDashboardPositionDataService positions = new();
            await positions.SaveOrderAsync(
                DashboardSnapshot.FavoritesSection, [second.Id, first.Id], TestContext.Current.CancellationToken);

            DashboardSnapshot snapshot = await LoadAsync(series, positions);

            // Die selbst gelegte Reihenfolge schlägt die alphabetische, sonst wäre das
            // Sortieren per Ziehen und Ablegen beim nächsten Öffnen vergessen.
            Assert.Equal(["TKKG", "Bibi Blocksberg"], Titles(snapshot));
        }

        [Fact]
        public async Task LoadAsync_WithPartialOrder_PutsPlacedSeriesFirst()
        {
            FakeSeriesDataService series = new();
            Series placed = await AddFavoriteAsync(series, "TKKG");
            _ = await AddFavoriteAsync(series, "Bibi Blocksberg");

            FakeDashboardPositionDataService positions = new();
            await positions.SaveOrderAsync(
                DashboardSnapshot.FavoritesSection, [placed.Id], TestContext.Current.CancellationToken);

            DashboardSnapshot snapshot = await LoadAsync(series, positions);

            // Eine neu hinzugekommene Serie hängt hinten an, statt die gelegte Reihenfolge
            // durcheinanderzubringen.
            Assert.Equal(["TKKG", "Bibi Blocksberg"], Titles(snapshot));
        }

        [Fact]
        public async Task LoadAsync_IgnoresOrdersOfOtherSections()
        {
            FakeSeriesDataService series = new();
            Series first = await AddFavoriteAsync(series, "Bibi Blocksberg");
            Series second = await AddFavoriteAsync(series, "TKKG");

            FakeDashboardPositionDataService positions = new();
            await positions.SaveOrderAsync(
                "Neuerscheinungen", [second.Id, first.Id], TestContext.Current.CancellationToken);

            DashboardSnapshot snapshot = await LoadAsync(series, positions);

            // Eine Reihenfolge aus einer älteren Fassung der Startseite darf die Favoriten
            // nicht mehr umstellen.
            Assert.Equal(["Bibi Blocksberg", "TKKG"], Titles(snapshot));
        }

        private static Task<DashboardSnapshot> LoadAsync(
            FakeSeriesDataService series, FakeDashboardPositionDataService positions)
            => DashboardSnapshot.LoadAsync(
                series,
                new FakePlaybackStateDataService(),
                new FakeAppSettingsDataService(),
                positions,
                startupResult: null,
                new FakeLogger());

        private static async Task<Series> AddFavoriteAsync(FakeSeriesDataService store, string title)
        {
            Series series = new() { Title = title, IsFavorite = true, IsSubscribed = true };
            await store.AddAsync(series, TestContext.Current.CancellationToken);
            return series;
        }

        private static List<string> Titles(DashboardSnapshot snapshot)
            => [.. snapshot.FavoriteSeries.Select(s => s.Title)];
    }
}
