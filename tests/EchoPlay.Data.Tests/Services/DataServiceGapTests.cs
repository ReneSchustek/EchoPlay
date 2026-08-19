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
    /// Prüft die Abfrage- und Schreibwege der Datendienste, die bisher nur von der
    /// Oberfläche aus benutzt wurden.
    /// </summary>
    /// <remarks>
    /// Die Suche über die Anbieter-Kennung ist der Weg, auf dem ein Import eine bereits
    /// vorhandene Serie wiedererkennt. Findet er sie nicht, legt er sie ein zweites Mal
    /// an — genau die Duplikate, die im Bestand des Anwenders stehen.
    /// </remarks>
    public sealed class DataServiceGapTests : DbTestBase
    {
        // ── Serien ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Series_AreFoundByTheirSpotifyArtistId()
        {
            SeriesDataService sut = CreateSeriesService();
            await sut.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg" },
                TestContext.Current.CancellationToken);

            Series? gefunden = await sut.GetBySpotifyArtistIdAsync("sp_tkkg", TestContext.Current.CancellationToken);
            Series? nicht = await sut.GetBySpotifyArtistIdAsync("sp_unbekannt", TestContext.Current.CancellationToken);

            Assert.NotNull(gefunden);
            Assert.Equal("TKKG", gefunden.Title);
            Assert.Null(nicht);
        }

        [Fact]
        public async Task Series_AreFoundByTheirAppleMusicArtistId()
        {
            SeriesDataService sut = CreateSeriesService();
            await sut.AddAsync(
                new Series { Title = "Die drei ???", AppleMusicArtistId = "am_ddf" },
                TestContext.Current.CancellationToken);

            Series? gefunden = await sut.GetByAppleMusicArtistIdAsync("am_ddf", TestContext.Current.CancellationToken);

            Assert.NotNull(gefunden);
            Assert.Equal("Die drei ???", gefunden.Title);
        }

        [Fact]
        public async Task Series_Update_KeepsTheChangedTitle()
        {
            SeriesDataService sut = CreateSeriesService();
            Series serie = new() { Title = "Alter Name" };
            await sut.AddAsync(serie, TestContext.Current.CancellationToken);

            serie.Title = "Neuer Name";
            await sut.UpdateAsync(serie, TestContext.Current.CancellationToken);

            Series? geladen = await sut.GetByIdAsync(serie.Id, TestContext.Current.CancellationToken);
            Assert.Equal("Neuer Name", geladen?.Title);
        }

        [Fact]
        public async Task Series_CoverLastChecked_IsStored()
        {
            SeriesDataService sut = CreateSeriesService();
            Series serie = new() { Title = "TKKG" };
            await sut.AddAsync(serie, TestContext.Current.CancellationToken);

            DateTime checkedAt = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
            await sut.SetCoverLastCheckedAsync(serie.Id, checkedAt, TestContext.Current.CancellationToken);

            // Ohne diesen Zeitstempel fragte der Hintergrundlauf dieselbe Serie bei jedem
            // Start erneut bei allen Anbietern an.
            Series? geladen = await Context.Series.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serie.Id, TestContext.Current.CancellationToken);
            Assert.Equal(checkedAt, geladen?.CoverLastChecked);
        }

        [Fact]
        public async Task Series_CoverLastChecked_ForAnUnknownSeries_ChangesNothing()
        {
            SeriesDataService sut = CreateSeriesService();

            await sut.SetCoverLastCheckedAsync(
                Guid.NewGuid(), DateTime.UtcNow, TestContext.Current.CancellationToken);

            Assert.Empty(await Context.Series.ToListAsync(TestContext.Current.CancellationToken));
        }

        // ── Folgen ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Episodes_AreAddedOneByOneAndInBatches()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            await sut.AddAsync(
                new Episode { SeriesId = serie.Id, Title = "Folge 1" },
                TestContext.Current.CancellationToken);

            await sut.AddRangeAsync(
            [
                new Episode { SeriesId = serie.Id, Title = "Folge 2" },
                new Episode { SeriesId = serie.Id, Title = "Folge 3" },
            ], TestContext.Current.CancellationToken);

            // Der Import legt zweihundert Folgen auf einmal an. Einzeln zu schreiben
            // kostet dabei zweihundert Rundläufe zur Datenbank.
            Assert.Equal(3, (await sut.GetBySeriesIdAsync(serie.Id, TestContext.Current.CancellationToken)).Count);
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
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode folge = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            DateTime checkedAt = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
            await sut.SetCoverLastCheckedAsync(folge.Id, checkedAt, TestContext.Current.CancellationToken);

            Episode? geladen = await sut.GetByIdAsync(folge.Id, TestContext.Current.CancellationToken);
            Assert.Equal(checkedAt, geladen?.CoverLastChecked);
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

        // ── Spuren ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task FirstTracks_ForNoEpisodes_AreEmpty()
        {
            LocalTrackDataService sut = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, LocalTrack> ersteSpuren =
                await sut.GetFirstTracksByEpisodeIdsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(ersteSpuren);
        }

        [Fact]
        public async Task FirstTracks_TakeTheLowestTrackNumberPerEpisode()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode folge = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            Context.LocalTracks.AddRange(
                new LocalTrack { EpisodeId = folge.Id, FilePath = @"D:\03.mp3", TrackNumber = 3 },
                new LocalTrack { EpisodeId = folge.Id, FilePath = @"D:\01.mp3", TrackNumber = 1 },
                new LocalTrack { EpisodeId = folge.Id, FilePath = @"D:\02.mp3", TrackNumber = 2 });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            LocalTrackDataService sut = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, LocalTrack> ersteSpuren =
                await sut.GetFirstTracksByEpisodeIdsAsync([folge.Id], TestContext.Current.CancellationToken);

            // Die erste Spur trägt das Bild aus der Kennzeichnung. Käme eine beliebige,
            // zeigte die Kachel das Bild aus der Mitte der Folge.
            Assert.Equal(@"D:\01.mp3", ersteSpuren[folge.Id].FilePath);
        }

        // ── Merkliste ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public async Task Remember_WithATitleThatLeavesNothing_StoresNothing(string titel)
        {
            WatchedTitleDataService sut = new(Context, NullLoggerFactory);

            await sut.RememberAsync(titel, TestContext.Current.CancellationToken);

            // Bliebe ein leerer Vergleichstitel stehen, löschte ein späteres Vergessen
            // alle Einträge mit leerem Vergleichstitel auf einmal.
            Assert.Empty(await Context.WatchedTitles.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public async Task Forget_WithATitleThatLeavesNothing_DeletesNothing(string titel)
        {
            WatchedTitleDataService sut = new(Context, NullLoggerFactory);
            await sut.RememberAsync("TKKG", TestContext.Current.CancellationToken);

            await sut.ForgetAsync(titel, TestContext.Current.CancellationToken);

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
            Series eine = await DataBuilder.PersistSeriesAsync("TKKG");
            Series zwei = await DataBuilder.PersistSeriesAsync("TKKG");
            eine.IsWatched = true;
            zwei.IsWatched = true;
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
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            serie.IsWatched = true;
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            WatchedTitleDataService sut = new(Context, NullLoggerFactory);
            _ = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            int nochmal = await sut.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, nochmal);
        }

        // ── Zwischenspeicher der Neuerscheinungen ────────────────────────────────

        [Fact]
        public async Task CachedReleases_UpsertingAKnownEntry_UpdatesItInPlace()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = serie.Id,
                    Title = "Folge 250",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    CheckedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ], TestContext.Current.CancellationToken);

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = serie.Id,
                    Title = "Folge 250 – Der neue Fall",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    CheckedAtUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                },
            ], TestContext.Current.CancellationToken);

            // Titel und Bild einer angekündigten Folge ändern sich bis zum Erscheinen
            // noch. Ein zweiter Eintrag stünde doppelt auf der Startseite.
            CachedNewRelease einzig = Assert.Single(
                await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Folge 250 – Der neue Fall", einzig.Title);
        }

        [Fact]
        public async Task CachedReleases_UpsertingASoftDeletedEntry_BringsItBack()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = serie.Id,
                    Title = "Folge 250",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    CheckedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ], TestContext.Current.CancellationToken);

            CachedNewRelease vorhanden = await Context.CachedNewReleases.AsTracking()
                .FirstAsync(TestContext.Current.CancellationToken);
            vorhanden.MarkAsDeleted(DateTime.UtcNow);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = serie.Id,
                    Title = "Folge 250",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    CheckedAtUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                },
            ], TestContext.Current.CancellationToken);

            // Der Zwischenspeicher trägt keine fachlichen Daten. Ein als gelöscht
            // markierter Eintrag darf eine erneut gemeldete Folge nicht dauerhaft
            // verstecken — sonst fehlt sie auf der Startseite, bis jemand aufräumt.
            CachedNewRelease wieder = Assert.Single(
                await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
            Assert.False(wieder.IsDeleted);
        }

        [Fact]
        public async Task CachedReleases_RemovingOldEntries_WithNothingOld_DeletesNothing()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService sut = new(Context, NullLoggerFactory);

            await sut.UpsertRangeAsync(
            [
                new CachedNewRelease
                {
                    SeriesId = serie.Id,
                    Title = "Folge 250",
                    EpisodeNumber = 250,
                    CollectionId = 250,
                    ReleaseDate = DateTime.UtcNow.AddDays(-1),
                    CheckedAtUtc = DateTime.UtcNow,
                },
            ], TestContext.Current.CancellationToken);

            int entfernt = await sut.RemoveOlderThanAsync(
                DateTime.UtcNow.AddDays(-30), TestContext.Current.CancellationToken);

            Assert.Equal(0, entfernt);
            Assert.NotEmpty(await Context.CachedNewReleases.ToListAsync(TestContext.Current.CancellationToken));
        }
    }
}
