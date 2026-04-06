using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält Tests zur Verifikation des Abfrageverhaltens von Serien.
    /// Der Fokus liegt auf der korrekten Anwendung des globalen Soft-Delete-QueryFilters.
    /// </summary>
    public class SeriesQueryTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass logisch gelöschte Serien nicht mehr über reguläre
        /// Abfragen zurückgegeben werden, während physisch vorhandene Datensätze
        /// weiterhin in der Datenbank verbleiben.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task GetAllAsync_ReturnsOnlyNotDeletedSeries()
        {
            // Eine aktive Serie dient als Referenzdatensatz, der sichtbar bleiben muss.
            _ = await DataBuilder.PersistSeriesAsync("Aktiv");

            // Diese Serie wird explizit logisch gelöscht und darf nicht mehr erscheinen.
            Series deletedSeries = await DataBuilder.PersistSeriesAsync("Gelöscht");

            // Das Soft-Delete erfolgt bewusst nach dem Persistieren, da der QueryFilter
            // ausschließlich auf gespeicherte Zustände wirkt.
            deletedSeries.MarkAsDeleted(DateTime.UtcNow);
            await Context.SaveChangesAsync();

            // Der ChangeTracker wird geleert, um sicherzustellen, dass die folgende
            // Abfrage tatsächlich über die Datenbank erfolgt und nicht auf bereits
            // getrackte Entitäten zurückgreift.
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);

            int expectedCount = 1;

            // Die Abfrage erfolgt ohne IgnoreQueryFilters, da hier explizit das
            // Standardverhalten der Anwendung geprüft wird.
            IReadOnlyList<Series> result = await service.GetAllAsync();

            // Es darf ausschließlich die nicht gelöschte Serie zurückgegeben werden.
            Assert.Equal(expectedCount, result.Count);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsSeries_WhenExists()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            Series? result = await service.GetByIdAsync(series.Id);

            Assert.NotNull(result);
            Assert.Equal("TKKG", result.Title);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
        {
            SeriesDataService service = new(Context, NullLoggerFactory);

            Series? result = await service.GetByIdAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetByAppleMusicArtistIdAsync_FindsSeries()
        {
            Series series = new() { Title = "Die drei ???", AppleMusicArtistId = "12345" };
            Context.Series.Add(series);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            Series? result = await service.GetByAppleMusicArtistIdAsync("12345");

            Assert.NotNull(result);
            Assert.Equal("Die drei ???", result.Title);
        }

        [Fact]
        public async Task GetByAppleMusicArtistIdAsync_ReturnsNull_WhenNotFound()
        {
            SeriesDataService service = new(Context, NullLoggerFactory);

            Series? result = await service.GetByAppleMusicArtistIdAsync("99999");

            Assert.Null(result);
        }

        [Fact]
        public async Task SetWatchedAsync_SetsFlag()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetWatchedAsync(series.Id, true);
            Context.ChangeTracker.Clear();

            Series? reloaded = await service.GetByIdAsync(series.Id);
            Assert.True(reloaded!.IsWatched);
        }

        [Fact]
        public async Task SetWatchedAsync_UnknownId_DoesNotThrow()
        {
            SeriesDataService service = new(Context, NullLoggerFactory);

            // Unbekannte ID → kein Fehler, nur Log-Warnung
            await service.SetWatchedAsync(Guid.NewGuid(), true);
        }
    }
}