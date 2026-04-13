using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SeriesDataService.SetLocalCoverAsync"/>.
    /// Prüft Persistenz und Überschreiben von manuell gespeicherten Cover-Binärdaten.
    /// </summary>
    public class SeriesCoverTests : DbTestBase
    {
        [Fact]
        public async Task SetLocalCoverAsync_PersistsCoverData()
        {
            // Bytes müssen nach dem Aufruf in der Datenbank abrufbar sein
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            byte[] coverBytes = [0xFF, 0xD8, 0xFF]; // minimaler JPEG-Header

            await service.SetLocalCoverAsync(series.Id, coverBytes);
            Context.ChangeTracker.Clear();

            Series? updated = await Context.Series.FindAsync(series.Id);
            Assert.Equal(coverBytes, updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_OverwritesExistingCover()
        {
            // Ein bereits gespeichertes Cover wird durch den neuen Wert ersetzt
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            series.LocalCoverData = [0x01, 0x02];
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            byte[] newBytes = [0xFF, 0xD8, 0xFF, 0xE0];

            await service.SetLocalCoverAsync(series.Id, newBytes);
            Context.ChangeTracker.Clear();

            Series? updated = await Context.Series.FindAsync(series.Id);
            Assert.Equal(newBytes, updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_Null_ClearsCover()
        {
            // Null-Übergabe muss das Cover löschen – wird beim Reset benötigt
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            series.LocalCoverData = [0xFF, 0xD8];
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetLocalCoverAsync(series.Id, null);
            Context.ChangeTracker.Clear();

            Series? updated = await Context.Series.FindAsync(series.Id);
            Assert.Null(updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_UnknownId_DoesNotThrow()
        {
            // Unbekannte ID wird stillschweigend ignoriert – kein Fehler, kein Crash
            SeriesDataService service = new(Context, NullLoggerFactory);

            await service.SetLocalCoverAsync(Guid.NewGuid(), [0x01]);
        }
    }
}
