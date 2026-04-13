using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für den Favoriten-Mechanismus der <see cref="SeriesDataService"/>-Klasse.
    /// Prüft Filterung, Persistenz und Sortierung rund um das <c>IsFavorite</c>-Flag.
    /// </summary>
    public class SeriesFavoriteTests : DbTestBase
    {
        [Fact]
        public async Task GetFavorites_ReturnsOnlyFavorites()
        {
            // Nicht favorisierte Serie darf nicht im Ergebnis erscheinen
            _ = await DataBuilder.PersistSeriesAsync("TKKG");

            Series favorited = await DataBuilder.PersistSeriesAsync("Die drei ???");
            favorited.IsFavorite = true;
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetFavoritesAsync();

            _ = Assert.Single(result);
            Assert.Equal("Die drei ???", result[0].Title);
        }

        [Fact]
        public async Task SetFavorite_PersistsFlag()
        {
            // Nach SetFavoriteAsync(true) muss IsFavorite in der DB true sein
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetFavoriteAsync(series.Id, true);

            // ChangeTracker leeren, damit der nächste FindAsync wirklich aus der DB liest
            Context.ChangeTracker.Clear();
            Series? updated = await Context.Series.FindAsync(series.Id);

            Assert.True(updated!.IsFavorite);
        }

        [Fact]
        public async Task GetFavorites_ExcludesSoftDeleted()
        {
            // Gelöschte Serien dürfen nicht zurückgegeben werden, auch wenn favorisiert
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            series.IsFavorite = true;
            series.MarkAsDeleted(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetFavoritesAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetFavorites_SortsAlphabetically()
        {
            // Favorisierte Serien werden alphabetisch sortiert zurückgegeben
            Series bibi = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            Series tkkg = await DataBuilder.PersistSeriesAsync("TKKG");
            Series drei = await DataBuilder.PersistSeriesAsync("Die drei ???");

            bibi.IsFavorite = true;
            tkkg.IsFavorite = true;
            drei.IsFavorite = true;
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetFavoritesAsync();

            Assert.Equal(3, result.Count);
            Assert.Equal("Bibi Blocksberg", result[0].Title);
            Assert.Equal("Die drei ???", result[1].Title);
            Assert.Equal("TKKG", result[2].Title);
        }

        [Fact]
        public async Task SetFavorite_UnknownId_DoesNotThrow()
        {
            // Unbekannte ID wird stillschweigend ignoriert – kein Fehler, kein Crash
            SeriesDataService service = new(Context, NullLoggerFactory);

            // Kein Assert nötig – der Test schlägt fehl wenn eine Exception geworfen wird
            await service.SetFavoriteAsync(new Guid("99999999-9999-9999-9999-999999999995"), true);
        }
    }
}
