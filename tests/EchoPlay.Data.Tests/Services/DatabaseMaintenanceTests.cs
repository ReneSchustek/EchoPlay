using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für granulare Bibliotheks-Reset-Methoden in <see cref="DatabaseMaintenanceService"/>.
    /// </summary>
    public sealed class DatabaseMaintenanceTests : DbTestBase
    {
        [Fact]
        public async Task ClearOnlineLibraryAsync_RemovesOnlyOnlineImportedSeries()
        {
            // Zwei Serien: eine online-importiert, eine lokal
            Series onlineSeries = new() { Title = "Online-Serie", IsOnlineImported = true };
            Series localSeries = new() { Title = "Lokale Serie", IsOnlineImported = false };
            Context.Series.AddRange(onlineSeries, localSeries);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Episode onlineEpisode = new() { SeriesId = onlineSeries.Id, Title = "Online-Folge" };
            Episode localEpisode = new() { SeriesId = localSeries.Id, Title = "Lokale Folge" };
            Context.Episodes.AddRange(onlineEpisode, localEpisode);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearOnlineLibraryAsync();

            List<Series> remaining = await Context.Series.IgnoreQueryFilters().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(remaining);
            Assert.Equal("Lokale Serie", remaining[0].Title);

            List<Episode> remainingEpisodes = await Context.Episodes.IgnoreQueryFilters().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(remainingEpisodes);
            Assert.Equal("Lokale Folge", remainingEpisodes[0].Title);
        }

        [Fact]
        public async Task ClearOnlineLibraryAsync_NoOnlineSeries_DoesNothing()
        {
            Series localSeries = new() { Title = "Nur Lokal", IsOnlineImported = false };
            _ = Context.Series.Add(localSeries);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearOnlineLibraryAsync();

            int count = await Context.Series.IgnoreQueryFilters().CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_RemovesLocalOnlySeries()
        {
            // Rein lokale Serie (nicht online-importiert) → wird gelöscht
            Series localOnly = new() { Title = "Nur Lokal", IsOnlineImported = false };
            _ = Context.Series.Add(localOnly);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Episode localEpisode = new() { SeriesId = localOnly.Id, Title = "Lokale Folge" };
            _ = Context.Episodes.Add(localEpisode);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearLocalLibraryAsync();

            int seriesCount = await Context.Series.IgnoreQueryFilters().CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, seriesCount);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_ClearsLocalPathsOnOnlineSeries()
        {
            // Online-importierte Serie mit lokalem Pfad → Pfad wird entfernt, Serie bleibt
            Series onlineSeries = new()
            {
                Title = "Online mit Pfad",
                IsOnlineImported = true,
                LocalFolderPath = @"C:\Hörspiele\Serie"
            };
            _ = Context.Series.Add(onlineSeries);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearLocalLibraryAsync();

            Series? updated = await Context.Series.IgnoreQueryFilters().FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(updated.LocalFolderPath);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_DeletesAllLocalTracks()
        {
            Series series = new() { Title = "Test", IsOnlineImported = true };
            _ = Context.Series.Add(series);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Episode episode = new() { SeriesId = series.Id, Title = "Folge" };
            _ = Context.Episodes.Add(episode);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = episode.Id,
                FilePath = @"C:\test.mp3",
                TrackNumber = 1
            });
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearLocalLibraryAsync();

            int trackCount = await Context.LocalTracks.IgnoreQueryFilters().CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, trackCount);
        }

        // ── Cover-Bereinigung bei Reset ─────────────────────────────────────────

        [Fact]
        public async Task ClearLibraryAsync_DeletesAllCovers()
        {
            // Cover für eine Serie anlegen, dann alles löschen
            Series series = new() { Title = "Test" };
            _ = Context.Series.Add(series);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = Context.CoverImages.Add(new CoverImage
            {
                EntityType = "Series",
                EntityId = series.Id,
                ImageData = [1, 2, 3]
            });
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearLibraryAsync();

            int coverCount = await Context.CoverImages.IgnoreQueryFilters().CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, coverCount);
        }

        [Fact]
        public async Task ClearOnlineLibraryAsync_DeletesOnlyOnlineCovers()
        {
            // Zwei Serien mit je einem Cover – nur Online-Cover soll gelöscht werden
            Series onlineSeries = new() { Title = "Online", IsOnlineImported = true };
            Series localSeries = new() { Title = "Lokal", IsOnlineImported = false };
            Context.Series.AddRange(onlineSeries, localSeries);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Context.CoverImages.AddRange(
                new CoverImage { EntityType = "Series", EntityId = onlineSeries.Id, ImageData = [1] },
                new CoverImage { EntityType = "Series", EntityId = localSeries.Id, ImageData = [2] }
            );
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearOnlineLibraryAsync();

            List<CoverImage> remaining = await Context.CoverImages.IgnoreQueryFilters().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(remaining);
            Assert.Equal(localSeries.Id, remaining[0].EntityId);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_DeletesLocalOnlyCovers()
        {
            // Rein lokale Serie mit Cover – Cover muss mitgelöscht werden
            Series localOnly = new() { Title = "Nur Lokal", IsOnlineImported = false };
            Series onlineSeries = new() { Title = "Online", IsOnlineImported = true };
            Context.Series.AddRange(localOnly, onlineSeries);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Context.CoverImages.AddRange(
                new CoverImage { EntityType = "Series", EntityId = localOnly.Id, ImageData = [1] },
                new CoverImage { EntityType = "Series", EntityId = onlineSeries.Id, ImageData = [2] }
            );
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);
            await service.ClearLocalLibraryAsync();

            List<CoverImage> remaining = await Context.CoverImages.IgnoreQueryFilters().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(remaining);
            Assert.Equal(onlineSeries.Id, remaining[0].EntityId);
        }
    }
}
