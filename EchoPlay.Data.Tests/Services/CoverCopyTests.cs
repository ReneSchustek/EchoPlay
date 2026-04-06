using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="CoverCopyService.CopyFromMatchingEpisodesAsync"/>.
    /// Prüft die Cover-Kopie per Raw SQL über die CoverImages-Tabelle
    /// mit verschiedenen Matching-Strategien.
    /// </summary>
    public class CoverCopyTests : DbTestBase
    {
        /// <summary>
        /// Legt einen CoverImage-Eintrag für eine Episode an.
        /// </summary>
        private async Task PersistEpisodeCoverAsync(Guid episodeId, byte[] imageData)
        {
            Context.CoverImages.Add(new CoverImage
            {
                EntityType = "Episode",
                EntityId = episodeId,
                ImageData = imageData
            });
            await Context.SaveChangesAsync();
        }

        /// <summary>
        /// Lädt den CoverImage-Eintrag für eine Episode aus der DB.
        /// </summary>
        private async Task<CoverImage?> GetEpisodeCoverAsync(Guid episodeId)
        {
            return await Context.CoverImages
                .AsNoTracking()
                .FirstOrDefaultAsync(ci => ci.EntityType == "Episode" && ci.EntityId == episodeId);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_MatchByNumber_CopiesCover()
        {
            // Quell-Serie hat Cover in CoverImages, Ziel-Serie (gleicher Name) nicht
            Series source = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode sourceEp = await DataBuilder.PersistEpisodeAsync(source, "Folge 1");
            sourceEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            await PersistEpisodeCoverAsync(sourceEp.Id, [0xFF, 0xD8, 0xFF]);

            Series target = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode targetEp = await DataBuilder.PersistEpisodeAsync(target, "Episode 1");
            targetEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(target.Id);

            Assert.Equal(1, copied);

            CoverImage? targetCover = await GetEpisodeCoverAsync(targetEp.Id);
            Assert.NotNull(targetCover);
            Assert.Equal([0xFF, 0xD8, 0xFF], targetCover!.ImageData);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_MatchByNumberAndTitle_CopiesCover()
        {
            // Gleicher Serienname + Folgennummer + Titel → exakter Match
            Series source = await DataBuilder.PersistSeriesAsync("Die drei ???");
            Episode sourceEp = await DataBuilder.PersistEpisodeAsync(source, "Der Super-Papagei");
            sourceEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            await PersistEpisodeCoverAsync(sourceEp.Id, [0x89, 0x50, 0x4E, 0x47]);

            Series target = await DataBuilder.PersistSeriesAsync("Die drei ???");
            Episode targetEp = await DataBuilder.PersistEpisodeAsync(target, "Der Super-Papagei");
            targetEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(target.Id);

            Assert.Equal(1, copied);

            CoverImage? targetCover = await GetEpisodeCoverAsync(targetEp.Id);
            Assert.NotNull(targetCover);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_NoMatchingSeries_ReturnsZero()
        {
            // Kein passender Serientitel → nichts zu kopieren
            Series source = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode sourceEp = await DataBuilder.PersistEpisodeAsync(source, "Folge 1");
            await Context.SaveChangesAsync();
            await PersistEpisodeCoverAsync(sourceEp.Id, [0xFF, 0xD8]);

            Series target = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            await DataBuilder.PersistEpisodeAsync(target, "Folge 1");
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(target.Id);

            Assert.Equal(0, copied);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_AllCoverPresent_ReturnsZero()
        {
            // Alle Episoden haben bereits Cover in CoverImages → nichts zu tun
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            await Context.SaveChangesAsync();
            await PersistEpisodeCoverAsync(episode.Id, [0xFF, 0xD8]);
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(series.Id);

            Assert.Equal(0, copied);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_UnknownSeriesId_ReturnsZero()
        {
            // Unbekannte Serie → 0, kein Fehler
            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(Guid.NewGuid());

            Assert.Equal(0, copied);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_DifferentSeriesName_DoesNotCopy()
        {
            // Anderer Serienname → kein Match auf keiner Stufe
            Series source = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode sourceEp = await DataBuilder.PersistEpisodeAsync(source, "Folge 1");
            sourceEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            await PersistEpisodeCoverAsync(sourceEp.Id, [0xAA, 0xBB]);

            Series target = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            Episode targetEp = await DataBuilder.PersistEpisodeAsync(target, "Folge 1");
            targetEp.EpisodeNumber = 1;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(target.Id);

            Assert.Equal(0, copied);
        }

        [Fact]
        public async Task CopyFromMatchingEpisodes_MultipleMissing_CopiesAll()
        {
            // Mehrere Episoden ohne Cover → alle werden kopiert
            Series source = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode sourceEp1 = await DataBuilder.PersistEpisodeAsync(source, "Folge 1");
            sourceEp1.EpisodeNumber = 1;

            Episode sourceEp2 = await DataBuilder.PersistEpisodeAsync(source, "Folge 2");
            sourceEp2.EpisodeNumber = 2;
            await Context.SaveChangesAsync();

            await PersistEpisodeCoverAsync(sourceEp1.Id, [0x01]);
            await PersistEpisodeCoverAsync(sourceEp2.Id, [0x02]);

            Series target = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode targetEp1 = await DataBuilder.PersistEpisodeAsync(target, "Folge 1");
            targetEp1.EpisodeNumber = 1;

            Episode targetEp2 = await DataBuilder.PersistEpisodeAsync(target, "Folge 2");
            targetEp2.EpisodeNumber = 2;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            CoverCopyService service = new(Context, NullLoggerFactory);
            int copied = await service.CopyFromMatchingEpisodesAsync(target.Id);

            Assert.Equal(2, copied);
        }

    }
}
