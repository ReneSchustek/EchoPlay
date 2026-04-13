using EchoPlay.Data.Context;
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
    }
}
