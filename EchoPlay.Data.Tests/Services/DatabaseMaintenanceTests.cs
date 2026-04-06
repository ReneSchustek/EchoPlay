using EchoPlay.Data.Entities.Library;
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
            await Context.SaveChangesAsync();

            Episode onlineEpisode = new() { SeriesId = onlineSeries.Id, Title = "Online-Folge" };
            Episode localEpisode = new() { SeriesId = localSeries.Id, Title = "Lokale Folge" };
            Context.Episodes.AddRange(onlineEpisode, localEpisode);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context);
            await service.ClearOnlineLibraryAsync();

            List<Series> remaining = await Context.Series.IgnoreQueryFilters().ToListAsync();
            Assert.Single(remaining);
            Assert.Equal("Lokale Serie", remaining[0].Title);

            List<Episode> remainingEpisodes = await Context.Episodes.IgnoreQueryFilters().ToListAsync();
            Assert.Single(remainingEpisodes);
            Assert.Equal("Lokale Folge", remainingEpisodes[0].Title);
        }

        [Fact]
        public async Task ClearOnlineLibraryAsync_NoOnlineSeries_DoesNothing()
        {
            Series localSeries = new() { Title = "Nur Lokal", IsOnlineImported = false };
            Context.Series.Add(localSeries);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context);
            await service.ClearOnlineLibraryAsync();

            int count = await Context.Series.IgnoreQueryFilters().CountAsync();
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_RemovesLocalOnlySeries()
        {
            // Rein lokale Serie (nicht online-importiert) → wird gelöscht
            Series localOnly = new() { Title = "Nur Lokal", IsOnlineImported = false };
            Context.Series.Add(localOnly);
            await Context.SaveChangesAsync();

            Episode localEpisode = new() { SeriesId = localOnly.Id, Title = "Lokale Folge" };
            Context.Episodes.Add(localEpisode);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context);
            await service.ClearLocalLibraryAsync();

            int seriesCount = await Context.Series.IgnoreQueryFilters().CountAsync();
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
            Context.Series.Add(onlineSeries);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context);
            await service.ClearLocalLibraryAsync();

            Series? updated = await Context.Series.IgnoreQueryFilters().FirstAsync();
            Assert.Null(updated.LocalFolderPath);
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_DeletesAllLocalTracks()
        {
            Series series = new() { Title = "Test", IsOnlineImported = true };
            Context.Series.Add(series);
            await Context.SaveChangesAsync();

            Episode episode = new() { SeriesId = series.Id, Title = "Folge" };
            Context.Episodes.Add(episode);
            await Context.SaveChangesAsync();

            Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = episode.Id,
                FilePath = @"C:\test.mp3",
                TrackNumber = 1
            });
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DatabaseMaintenanceService service = new(Context);
            await service.ClearLocalLibraryAsync();

            int trackCount = await Context.LocalTracks.IgnoreQueryFilters().CountAsync();
            Assert.Equal(0, trackCount);
        }
    }
}
