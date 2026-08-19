using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Prüft das endgültige Entfernen gelöschter Einträge und die beiden Pflegebefehle
    /// gegen SQLite.
    /// </summary>
    /// <remarks>
    /// Gelöschte Serien und Folgen bleiben zunächst nur als gelöscht markiert stehen —
    /// das macht ein Versehen rückgängig. Ohne die endgültige Bereinigung wüchse die
    /// Datenbank aber unbegrenzt weiter. Die Reihenfolge ist dabei nicht frei wählbar:
    /// Solange Spuren oder Hörstände auf eine Folge zeigen, verweigert SQLite deren
    /// Löschung.
    /// </remarks>
    public sealed class DatabasePurgeTests : DbTestBase
    {
        [Fact]
        public async Task Purge_RemovesADeletedEpisodeWithItsTracksAndProgress()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode folge = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = folge.Id,
                FilePath = @"D:\Media\TKKG\01.mp3",
                TrackNumber = 1,
            });
            _ = Context.PlaybackStates.Add(new PlaybackState { EpisodeId = folge.Id });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            folge.MarkAsDeleted(DateTime.UtcNow.AddDays(-40));
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            await sut.PurgeAsync(retentionDays: 30);

            // Erst die Kinder, dann die Folge — sonst blockiert der Fremdschlüssel.
            Assert.Empty(await Context.LocalTracks.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await Context.PlaybackStates.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await Context.Episodes.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Purge_RemovesADeletedSeriesWithEpisodesThatAreNotMarkedThemselves()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode folge = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = folge.Id,
                FilePath = @"D:\Media\TKKG\01.mp3",
                TrackNumber = 1,
            });
            _ = Context.PlaybackStates.Add(new PlaybackState { EpisodeId = folge.Id });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            serie.MarkAsDeleted(DateTime.UtcNow.AddDays(-40));
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            await sut.PurgeAsync(retentionDays: 30);

            // Wird eine Serie gelöscht, tragen ihre Folgen die Markierung nicht zwingend
            // selbst. Über sie hinweg zu räumen ist der eigentliche Zweck dieser Phase.
            Assert.Empty(await Context.Series.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await Context.Episodes.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await Context.LocalTracks.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Purge_KeepsWhatWasDeletedRecently()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            _ = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            serie.MarkAsDeleted(DateTime.UtcNow.AddDays(-3));
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            await sut.PurgeAsync(retentionDays: 30);

            // Die Aufbewahrungsfrist ist die Rückholmöglichkeit des Anwenders. Innerhalb
            // dieser Zeit darf nichts endgültig verschwinden.
            Assert.NotEmpty(await Context.Series.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Purge_KeepsWhatIsNotDeleted()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            _ = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            await sut.PurgeAsync(retentionDays: 0);

            Assert.NotEmpty(await Context.Series.ToListAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await Context.Episodes.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Optimize_RunsAgainstTheDatabase()
        {
            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            // Der Befehl aktualisiert die Statistiken des Abfrageplaners. Er läuft beim
            // Beenden — scheitert er dort, bliebe das Fenster offen.
            await sut.OptimizeAsync();
        }

        [Fact]
        public async Task Vacuum_RunsAgainstTheDatabase()
        {
            DatabaseMaintenanceService sut = new(Context, NullLoggerFactory);

            await sut.VacuumAsync();
        }
    }
}
