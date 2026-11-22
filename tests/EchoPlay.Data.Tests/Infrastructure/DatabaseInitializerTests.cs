using EchoPlay.Data.Configurations;
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
    /// angewandt werden. Fehlt zu einer Migration die <c>.Designer.cs</c>, kennt EF Core sie
    /// nicht, der Start läuft trotzdem durch und erst der erste Zugriff auf die neue Spalte
    /// scheitert, und genau das fangen diese Tests vor der Auslieferung.
    /// </summary>
    public sealed class DatabaseInitializerTests : IAsyncLifetime, IDisposable
    {
        // Die Datenbank lebt nur im Speicher; eine Datei, die es nicht gibt, lässt den
        // Initialisierer die Sicherung überspringen, wie bei einer Erstinstallation.
        private static readonly DatabaseFileLocation NoDatabaseFile =
            new(Path.Combine(Path.GetTempPath(), "EchoPlayDbInitTests-ohne-Datei", "echoplay.db"));

        private SqliteConnection? _connection;
        private EchoPlayDbContext? _context;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            await _connection.OpenAsync(cancellationToken: TestContext.Current.CancellationToken);

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(_connection)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            _context = new EchoPlayDbContext(builder.Options);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
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
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            IEnumerable<string> applied = await _context!.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken);
            IEnumerable<string> pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEmpty(applied);
            Assert.Empty(pending);
        }

        [Fact]
        public async Task InitializeAsync_Creates_Expected_Columns_From_Recent_Migrations()
        {
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            // Die Schemaerweiterungen dieses Blocks: die Spalte SourceHash auf CoverImages
            // (Prüfsumme SHA-256), die Tabelle SecureSettings (Spotify-Zugangsdaten per DPAPI)
            // und SpotifyAlbumId/AppleMusicAlbumId auf Episodes.
            using SqliteCommand cmd = _connection!.CreateCommand();

            cmd.CommandText = "SELECT name FROM pragma_table_info('CoverImages') WHERE name = 'SourceHash';";
            Assert.Equal("SourceHash", await cmd.ExecuteScalarAsync(cancellationToken: TestContext.Current.CancellationToken));

            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SecureSettings';";
            Assert.Equal("SecureSettings", await cmd.ExecuteScalarAsync(cancellationToken: TestContext.Current.CancellationToken));

            cmd.CommandText = "SELECT name FROM pragma_table_info('Episodes') WHERE name = 'SpotifyAlbumId';";
            Assert.Equal("SpotifyAlbumId", await cmd.ExecuteScalarAsync(cancellationToken: TestContext.Current.CancellationToken));

            cmd.CommandText = "SELECT name FROM pragma_table_info('Episodes') WHERE name = 'AppleMusicAlbumId';";
            Assert.Equal("AppleMusicAlbumId", await cmd.ExecuteScalarAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task InitializeAsync_Is_Idempotent()
        {
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);
            int firstAppliedCount = (await _context!.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count();

            // Zweiter Aufruf darf kein neues Schema anwenden und keine Fehler werfen.
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);
            int secondAppliedCount = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count();

            Assert.Equal(firstAppliedCount, secondAppliedCount);
            Assert.Empty(await _context.Database.GetPendingMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task InitializeAsync_Registers_Consolidated_Migration()
        {
            // AddSourceHashSecureSettingsProviderIds bündelt drei Schemaänderungen (Prüfsumme der
            // Cover, verschlüsselte Zugangsdaten, Anbieterkennungen) und gilt als Breaking Change.
            // Der Test hält fest, dass der Start sie anwendet.
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            IEnumerable<string> applied = await _context!.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains(applied, id => id.Contains("AddSourceHashSecureSettingsProviderIds", StringComparison.Ordinal));
        }

        [Fact]
        public async Task InitializeAsync_Creates_Expected_Indexes_With_Filters()
        {
            // Verifiziert, dass die Migrationskette die Teilindizes mit ihren WHERE-Klauseln in
            // sqlite_master schreibt, und zwar in der Form, die der Löschfilter von EF Core
            // schreibt (siehe SoftDeleteIndexFilter); mit IsDeleted = 0 wären sie unbrauchbar.
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

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
            using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken: TestContext.Current.CancellationToken))
                {
                    indexes[reader.GetString(0)] = reader.GetString(1);
                }
            }

            Assert.Contains(SoftDeleteIndexFilter.ActiveRows + " AND LastPlayedAt IS NOT NULL",
                indexes["IX_PlaybackStates_LastPlayedAt"], StringComparison.Ordinal);
            Assert.Contains(SoftDeleteIndexFilter.ActiveRows + " AND ReleaseDate IS NOT NULL",
                indexes["IX_Episodes_ReleaseDate"], StringComparison.Ordinal);
            Assert.Contains("UNIQUE", indexes["IX_CoverImages_EntityType_EntityId"], StringComparison.Ordinal);
            Assert.Contains(SoftDeleteIndexFilter.ActiveRows, indexes["IX_CoverImages_EntityType_EntityId"], StringComparison.Ordinal);
            Assert.Contains("UNIQUE", indexes["IX_SecureSettings_Key"], StringComparison.Ordinal);
            Assert.Contains(SoftDeleteIndexFilter.ActiveRows, indexes["IX_SecureSettings_Key"], StringComparison.Ordinal);
            Assert.DoesNotContain(indexes.Values, sql => sql.Contains("IsDeleted = 0", StringComparison.Ordinal));
        }

        /// <summary>
        /// Legt je einen Datensatz in den Tabellen an, die eine Migration am ehesten zerlegt:
        /// Serie, Folge, Hörstand und Cover.
        /// </summary>
        private static async Task SeedSeriesWithEpisodeStateAndCoverAsync(EchoPlayDbContext context)
        {
            Series series = new() { Title = "Migration-Fixture" };
            _ = context.Series.Add(series);
            _ = await context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Episode episode = new()
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                ReleaseDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            _ = context.Episodes.Add(episode);
            _ = await context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = context.PlaybackStates.Add(new PlaybackState
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc)
            });
            _ = context.CoverImages.Add(new CoverImage
            {
                EntityType = "Series",
                EntityId = series.Id,
                ImageData = [0x01, 0x02, 0x03]
            });
            _ = await context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task InitializeAsync_With_Existing_Fixtures_Stays_Stable()
        {
            // Migration darf eine DB mit bestehenden Daten (Episode + PlaybackState + CoverImage)
            // nicht zerlegen. EnsureCreated würde nur das aktuelle Modell anlegen. Wir nutzen
            // Den produktiven Initializer-Pfad, der die volle Migrationskette abspielt.
            DatabaseInitializer initializer = new(_context!, NoDatabaseFile);
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            await SeedSeriesWithEpisodeStateAndCoverAsync(_context!);

            // Zweiter InitializeAsync-Aufruf darf an bestehenden Daten nicht scheitern.
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Empty(await _context!.Database.GetPendingMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, await _context.Episodes.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, await _context.PlaybackStates.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, await _context.CoverImages.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }
}
