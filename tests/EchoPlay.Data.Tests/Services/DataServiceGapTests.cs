using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Prüft die Abfrage- und Schreibwege der Datendienste, die sonst nur die
    /// Oberfläche benutzt.
    /// </summary>
    /// <remarks>
    /// Die Suche über die Anbieter-Kennung ist der Weg, auf dem ein Import eine bereits
    /// vorhandene Serie wiedererkennt. Findet er sie nicht, legt er sie ein zweites Mal
    /// an, genau die Duplikate, die im Bestand des Anwenders stehen.
    /// </remarks>
    public sealed class DataServiceGapTests : DbTestBase
    {
        [Fact]
        public async Task Series_AreFoundByTheirSpotifyArtistId()
        {
            SeriesDataService sut = CreateSeriesService();
            await sut.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg" },
                TestContext.Current.CancellationToken);

            Series? found = await sut.GetBySpotifyArtistIdAsync("sp_tkkg", TestContext.Current.CancellationToken);
            Series? missing = await sut.GetBySpotifyArtistIdAsync("sp_unbekannt", TestContext.Current.CancellationToken);

            Assert.NotNull(found);
            Assert.Equal("TKKG", found.Title);
            Assert.Null(missing);
        }

        [Fact]
        public async Task Series_AreFoundByTheirAppleMusicArtistId()
        {
            SeriesDataService sut = CreateSeriesService();
            await sut.AddAsync(
                new Series { Title = "Die drei ???", AppleMusicArtistId = "am_ddf" },
                TestContext.Current.CancellationToken);

            Series? found = await sut.GetByAppleMusicArtistIdAsync("am_ddf", TestContext.Current.CancellationToken);

            Assert.NotNull(found);
            Assert.Equal("Die drei ???", found.Title);
        }

        [Fact]
        public async Task Series_Update_KeepsTheChangedTitle()
        {
            SeriesDataService sut = CreateSeriesService();
            Series series = new() { Title = "Alter Name" };
            await sut.AddAsync(series, TestContext.Current.CancellationToken);

            series.Title = "Neuer Name";
            await sut.UpdateAsync(series, TestContext.Current.CancellationToken);

            Series? loaded = await sut.GetByIdAsync(series.Id, TestContext.Current.CancellationToken);
            Assert.Equal("Neuer Name", loaded?.Title);
        }

        [Fact]
        public async Task Series_CoverLastChecked_IsStored()
        {
            SeriesDataService sut = CreateSeriesService();
            Series series = new() { Title = "TKKG" };
            await sut.AddAsync(series, TestContext.Current.CancellationToken);

            DateTime checkedAt = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
            await sut.SetCoverLastCheckedAsync(series.Id, checkedAt, TestContext.Current.CancellationToken);

            // Ohne diesen Zeitstempel fragte der Hintergrundlauf dieselbe Serie bei jedem
            // Start erneut bei allen Anbietern an.
            Series? loaded = await Context.Series.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == series.Id, TestContext.Current.CancellationToken);
            Assert.Equal(checkedAt, loaded?.CoverLastChecked);
        }

        [Fact]
        public async Task Series_CoverLastChecked_ForAnUnknownSeries_ChangesNothing()
        {
            SeriesDataService sut = CreateSeriesService();

            await sut.SetCoverLastCheckedAsync(
                Guid.NewGuid(), DateTime.UtcNow, TestContext.Current.CancellationToken);

            Assert.Empty(await Context.Series.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Episodes_AreAddedOneByOneAndInBatches()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            await sut.AddAsync(
                new Episode { SeriesId = series.Id, Title = "Folge 1" },
                TestContext.Current.CancellationToken);

            await sut.AddRangeAsync(
            [
                new Episode { SeriesId = series.Id, Title = "Folge 2" },
                new Episode { SeriesId = series.Id, Title = "Folge 3" },
            ], TestContext.Current.CancellationToken);

            // Der Import legt zweihundert Folgen auf einmal an. Einzeln zu schreiben
            // kostet dabei zweihundert Rundläufe zur Datenbank.
            Assert.Equal(3, (await sut.GetBySeriesIdAsync(series.Id, TestContext.Current.CancellationToken)).Count);
        }

        [Fact]
        public async Task Episodes_AddRange_WithAnEmptyList_DoesNothing()
        {
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            await sut.AddRangeAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(await Context.Episodes.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Episodes_CoverLastChecked_IsStored()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            DateTime checkedAt = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
            await sut.SetCoverLastCheckedAsync(episode.Id, checkedAt, TestContext.Current.CancellationToken);

            Episode? loaded = await sut.GetByIdAsync(episode.Id, TestContext.Current.CancellationToken);
            Assert.Equal(checkedAt, loaded?.CoverLastChecked);
        }

        [Fact]
        public async Task Episodes_CoverLastChecked_ForAnUnknownEpisode_ChangesNothing()
        {
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            await sut.SetCoverLastCheckedAsync(
                Guid.NewGuid(), DateTime.UtcNow, TestContext.Current.CancellationToken);

            Assert.Empty(await Context.Episodes.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Episodes_CountsForNoSeries_AreEmpty()
        {
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, (int Total, int Local)> zahlen =
                await sut.GetEpisodeCountsForSeriesAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(zahlen);
        }

        [Fact]
        public async Task FirstTracks_ForNoEpisodes_AreEmpty()
        {
            LocalTrackDataService sut = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await sut.GetFirstTracksByEpisodeIdsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(firstTracks);
        }

        [Fact]
        public async Task FirstTracks_TakeTheLowestTrackNumberPerEpisode()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            Context.LocalTracks.AddRange(
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"D:\03.mp3", TrackNumber = 3 },
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"D:\01.mp3", TrackNumber = 1 },
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"D:\02.mp3", TrackNumber = 2 });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            LocalTrackDataService sut = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await sut.GetFirstTracksByEpisodeIdsAsync([episode.Id], TestContext.Current.CancellationToken);

            // Die erste Spur trägt das Bild aus der Kennzeichnung. Käme eine beliebige,
            // zeigte die Kachel das Bild aus der Mitte der Folge.
            Assert.Equal(@"D:\01.mp3", firstTracks[episode.Id].FilePath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public async Task Remember_WithATitleThatLeavesNothing_StoresNothing(string title)
        {
            WatchedTitleDataService sut = new(Context, NullLoggerFactory);

            await sut.RememberAsync(title, TestContext.Current.CancellationToken);

            // Bliebe ein leerer Vergleichstitel stehen, löschte ein späteres Vergessen
            // alle Einträge mit leerem Vergleichstitel auf einmal.
            Assert.Empty(await Context.WatchedTitles.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public async Task Forget_WithATitleThatLeavesNothing_DeletesNothing(string title)
        {
            WatchedTitleDataService sut = new(Context, NullLoggerFactory);
            await sut.RememberAsync("TKKG", TestContext.Current.CancellationToken);

            await sut.ForgetAsync(title, TestContext.Current.CancellationToken);

            Assert.NotEmpty(await Context.WatchedTitles.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Sync_WithoutAnyWatchedSeries_TakesNothing()
        {
            WatchedTitleDataService sut = new(Context, NullLoggerFactory);

            int taken = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, taken);
        }

        [Fact]
        public async Task Sync_WithDuplicateTitles_TakesEachOnlyOnce()
        {
            Series one = await DataBuilder.PersistSeriesAsync("TKKG");
            Series two = await DataBuilder.PersistSeriesAsync("TKKG");
            one.IsWatched = true;
            two.IsWatched = true;
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            WatchedTitleDataService sut = new(Context, NullLoggerFactory);

            int taken = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            // Der Bestand des Anwenders enthält Serien-Duplikate mit gleichem Titel.
            // Zweimal denselben Vergleichstitel zu schreiben verletzt den eindeutigen Index.
            Assert.Equal(1, taken);
            _ = Assert.Single(await Context.WatchedTitles.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Sync_WhenEverythingIsAlreadyRemembered_TakesNothing()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            series.IsWatched = true;
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            WatchedTitleDataService sut = new(Context, NullLoggerFactory);
            _ = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            int nochmal = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, nochmal);
        }

        /// <summary>Folge 250 der Serie im Zwischenspeicher, geprüft zum angegebenen Zeitpunkt.</summary>
        private static CachedNewRelease Episode250(Series series, DateTime checkedAt, string title = "Folge 250")
            => new()
            {
                SeriesId = series.Id,
                Title = title,
                EpisodeNumber = 250,
                CollectionId = 250,
                CheckedAtUtc = checkedAt,
            };

        [Fact]
        public async Task CachedReleases_UpsertingAKnownEntry_UpdatesItInPlace()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
                [Episode250(series, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))], TestContext.Current.CancellationToken);

            await sut.UpsertRangeAsync(
                [Episode250(series, new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc), "Folge 250 – Der neue Fall")],
                TestContext.Current.CancellationToken);

            // Titel und Bild einer angekündigten Folge ändern sich bis zum Erscheinen
            // noch. Ein zweiter Eintrag stünde doppelt auf der Startseite.
            CachedNewRelease einzig = Assert.Single(
                await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Folge 250 – Der neue Fall", einzig.Title);
        }

        [Fact]
        public async Task CachedReleases_UpsertingASoftDeletedEntry_BringsItBack()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
                [Episode250(series, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))], TestContext.Current.CancellationToken);

            CachedNewRelease existing = await Context.CachedNewReleases.AsTracking()
                .FirstAsync(TestContext.Current.CancellationToken);
            existing.MarkAsDeleted(DateTime.UtcNow);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await sut.UpsertRangeAsync(
                [Episode250(series, new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc))], TestContext.Current.CancellationToken);

            // Der Zwischenspeicher trägt keine fachlichen Daten. Ein als gelöscht
            // markierter Eintrag darf eine erneut gemeldete Folge nicht dauerhaft
            // verstecken, sonst fehlt sie auf der Startseite, bis jemand aufräumt.
            CachedNewRelease again = Assert.Single(
                await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
            Assert.False(again.IsDeleted);
        }

        [Fact]
        public async Task CachedReleases_RemovingOldEntries_WithNothingOld_DeletesNothing()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 250",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    ReleaseDate = DateTime.UtcNow.AddDays(-1),
                    CheckedAtUtc = DateTime.UtcNow,
                },
            ], TestContext.Current.CancellationToken);

            int removed = await sut.RemoveOlderThanAsync(
                DateTime.UtcNow.AddDays(-30), TestContext.Current.CancellationToken);

            Assert.Equal(0, removed);
            Assert.NotEmpty(await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
        }
    }
}
