using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Tests für <see cref="DatabaseInitializer"/>. Verifiziert, dass die Migrations-Kette
    /// auf einer leeren SQLite-Datenbank vollständig durchläuft und alle neueren Migrationen
    /// (inklusive der konsolidierten <c>AddSourceHashSecureSettingsProviderIds</c>-Migration)
    /// angewandt werden. Deckt damit genau den Silent-Failure-Fall ab, der am 2026-04-13
    /// in Produktion auftrat (fehlende Designer.cs-Dateien → Migrationen von EF Core nicht erkannt).
    /// </summary>
    public sealed class DatabaseInitializerTests : IAsyncLifetime, IDisposable
    {
        private SqliteConnection? _connection;
        private EchoPlayDbContext? _context;

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            await _connection.OpenAsync();

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(_connection)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            _context = new EchoPlayDbContext(builder.Options);
        }

        /// <inheritdoc/>
        public async Task DisposeAsync()
        {
            if (_context is not null)
            {
                await _context.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _context?.Dispose();
            _connection?.Dispose();
        }

        [Fact]
        public async Task InitializeAsync_On_Empty_Db_Applies_All_Migrations()
        {
            DatabaseInitializer initializer = new(_context!);

            await initializer.InitializeAsync();

            IEnumerable<string> applied = await _context!.Database.GetAppliedMigrationsAsync();
            IEnumerable<string> pending = await _context.Database.GetPendingMigrationsAsync();

            Assert.NotEmpty(applied);
            Assert.Empty(pending);
        }

        [Fact]
        public async Task InitializeAsync_Creates_Expected_Columns_From_Recent_Migrations()
        {
            DatabaseInitializer initializer = new(_context!);

            await initializer.InitializeAsync();

            // Reflektion der Schema-Änderungen aus den drei Migrationen, die heute hinzugefügt wurden:
            // - SourceHash auf CoverImages (Brief 227)
            // - SecureSettings-Tabelle (Brief 229)
            // - SpotifyAlbumId/AppleMusicAlbumId auf Episodes (Brief 230)
            using SqliteCommand cmd = _connection!.CreateCommand();

            cmd.CommandText = "SELECT name FROM pragma_table_info('CoverImages') WHERE name = 'SourceHash';";
            Assert.Equal("SourceHash", await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SecureSettings';";
            Assert.Equal("SecureSettings", await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT name FROM pragma_table_info('Episodes') WHERE name = 'SpotifyAlbumId';";
            Assert.Equal("SpotifyAlbumId", await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT name FROM pragma_table_info('Episodes') WHERE name = 'AppleMusicAlbumId';";
            Assert.Equal("AppleMusicAlbumId", await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task InitializeAsync_Is_Idempotent()
        {
            DatabaseInitializer initializer = new(_context!);

            await initializer.InitializeAsync();
            int firstAppliedCount = (await _context!.Database.GetAppliedMigrationsAsync()).Count();

            // Zweiter Aufruf darf kein neues Schema anwenden und keine Fehler werfen.
            await initializer.InitializeAsync();
            int secondAppliedCount = (await _context.Database.GetAppliedMigrationsAsync()).Count();

            Assert.Equal(firstAppliedCount, secondAppliedCount);
            Assert.Empty(await _context.Database.GetPendingMigrationsAsync());
        }

        [Fact]
        public async Task InitializeAsync_Registers_Consolidated_Migration()
        {
            // Regressions-Schutz: die am 2026-04-13 konsolidierte Migration muss als angewandt gelten.
            DatabaseInitializer initializer = new(_context!);

            await initializer.InitializeAsync();

            IEnumerable<string> applied = await _context!.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, id => id.Contains("AddSourceHashSecureSettingsProviderIds", StringComparison.Ordinal));
        }

        [Fact]
        public async Task InitializeAsync_Creates_Brief_274_Indexes_With_Filters()
        {
            // Verifiziert, dass die Migration AddSortIndexesAndSoftDeleteFilters
            // exakt die vier neuen Indizes mit ihren WHERE-Klauseln in sqlite_master schreibt.
            DatabaseInitializer initializer = new(_context!);

            await initializer.InitializeAsync();

            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                SELECT name, sql FROM sqlite_master
                WHERE type = 'index' AND name IN (
                    'IX_PlaybackStates_LastPlayedAt',
                    'IX_Episodes_ReleaseDate',
                    'IX_CoverImages_EntityType_EntityId',
                    'IX_SecureSettings_Key');
                """;

            Dictionary<string, string> indexes = new(StringComparer.Ordinal);
            using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    indexes[reader.GetString(0)] = reader.GetString(1);
                }
            }

            Assert.Contains("IsDeleted = 0 AND LastPlayedAt IS NOT NULL",
                indexes["IX_PlaybackStates_LastPlayedAt"], StringComparison.Ordinal);
            Assert.Contains("IsDeleted = 0 AND ReleaseDate IS NOT NULL",
                indexes["IX_Episodes_ReleaseDate"], StringComparison.Ordinal);
            Assert.Contains("UNIQUE", indexes["IX_CoverImages_EntityType_EntityId"], StringComparison.Ordinal);
            Assert.Contains("IsDeleted = 0", indexes["IX_CoverImages_EntityType_EntityId"], StringComparison.Ordinal);
            Assert.Contains("UNIQUE", indexes["IX_SecureSettings_Key"], StringComparison.Ordinal);
            Assert.Contains("IsDeleted = 0", indexes["IX_SecureSettings_Key"], StringComparison.Ordinal);
        }

        [Fact]
        public async Task InitializeAsync_With_Existing_Fixtures_Stays_Stable()
        {
            // Spiegelt die Pflicht aus Brief 274 wider: Migration darf eine DB
            // mit bestehenden Daten (Episode + PlaybackState + CoverImage) nicht zerlegen.
            // EnsureCreated würde nur das aktuelle Modell anlegen – wir nutzen den
            // produktiven Initializer-Pfad, der die volle Migrationskette abspielt.
            DatabaseInitializer initializer = new(_context!);
            await initializer.InitializeAsync();

            Series series = new() { Title = "Brief274-Fixture" };
            _ = _context!.Series.Add(series);
            _ = await _context.SaveChangesAsync();

            Episode episode = new()
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                ReleaseDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            _ = _context.Episodes.Add(episode);
            _ = await _context.SaveChangesAsync();

            PlaybackState state = new()
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc)
            };
            _ = _context.PlaybackStates.Add(state);

            CoverImage cover = new()
            {
                EntityType = "Series",
                EntityId = series.Id,
                ImageData = [0x01, 0x02, 0x03]
            };
            _ = _context.CoverImages.Add(cover);
            _ = await _context.SaveChangesAsync();

            // Zweiter InitializeAsync-Aufruf darf an bestehenden Daten nicht scheitern.
            await initializer.InitializeAsync();

            Assert.Empty(await _context.Database.GetPendingMigrationsAsync());
            Assert.Equal(1, await _context.Episodes.CountAsync());
            Assert.Equal(1, await _context.PlaybackStates.CountAsync());
            Assert.Equal(1, await _context.CoverImages.CountAsync());
        }
    }
}
