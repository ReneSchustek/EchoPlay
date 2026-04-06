using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeDataService.SetLocalCoverAsync"/>.
    /// Prüft Persistenz und Überschreiben von manuell gespeicherten Cover-Binärdaten auf Episodenebene.
    /// </summary>
    public class EpisodeCoverTests : DbTestBase
    {
        [Fact]
        public async Task SetLocalCoverAsync_PersistsCoverData()
        {
            // Bytes müssen nach dem Aufruf in der Datenbank abrufbar sein
            Series series   = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            byte[] coverBytes = [0xFF, 0xD8, 0xFF]; // minimaler JPEG-Header

            await service.SetLocalCoverAsync(episode.Id, coverBytes);
            Context.ChangeTracker.Clear();

            Episode? updated = await Context.Episodes.FindAsync(episode.Id);
            Assert.Equal(coverBytes, updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_OverwritesExistingCover()
        {
            // Vorhandenes Cover wird durch den neuen Wert ersetzt
            Series series   = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            episode.LocalCoverData = [0x01, 0x02];
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            byte[] newBytes = [0xFF, 0xD8, 0xFF, 0xE0];

            await service.SetLocalCoverAsync(episode.Id, newBytes);
            Context.ChangeTracker.Clear();

            Episode? updated = await Context.Episodes.FindAsync(episode.Id);
            Assert.Equal(newBytes, updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_Null_ClearsCover()
        {
            // Null-Übergabe muss das Cover löschen – wird beim Neu-Initialisierungs-Reset benötigt
            Series series   = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            episode.LocalCoverData = [0xFF, 0xD8];
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            await service.SetLocalCoverAsync(episode.Id, null);
            Context.ChangeTracker.Clear();

            Episode? updated = await Context.Episodes.FindAsync(episode.Id);
            Assert.Null(updated!.LocalCoverData);
        }

        [Fact]
        public async Task SetLocalCoverAsync_UnknownId_DoesNotThrow()
        {
            // Unbekannte ID wird stillschweigend ignoriert
            EpisodeDataService service = new(Context, NullLoggerFactory);

            await service.SetLocalCoverAsync(Guid.NewGuid(), [0x01]);
        }
    }
}
