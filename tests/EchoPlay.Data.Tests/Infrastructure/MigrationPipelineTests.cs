using EchoPlay.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Führt die Migrationskette echt aus – im Gegensatz zum restlichen Test-Bestand, der Schemas
    /// per <c>EnsureCreated</c> aus dem Modell erzeugt und Migrationen damit nie anfasst.
    /// Deckt genau die Lücke ab, durch die eine fehlerhafte Migration bisher erst zur Laufzeit
    /// aufgefallen wäre (fehlende Designer-Datei, ungültiges SQL, kaputte Reihenfolge).
    /// </summary>
    public sealed class MigrationPipelineTests
    {
        // Letzte Migration vor der Backfill-Migration – Startpunkt für den Datenbestands-Test.
        private const string BeforeBackfill = "20260721141222_AddOnlineEpisodeSortIndex";

        // Letzte Migration vor der Dubletten-Bereinigung – Startpunkt für den Bestands-Test.
        private const string BeforeDuplicateCleanup = "20260824102217_AusgeblendeteHinweise";

        private static SqliteConnection OpenConnection()
        {
            // Offene Verbindung hält die In-Memory-DB über alle Migrationsschritte am Leben.
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();
            return connection;
        }

        private static EchoPlayDbContext CreateContext(SqliteConnection connection)
        {
            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(connection);
            return new EchoPlayDbContext(builder.Options);
        }

        [Fact]
        public async Task Migrate_AppliesCompleteChain_WithoutPendingRemainder()
        {
            using SqliteConnection connection = OpenConnection();
            using EchoPlayDbContext context = CreateContext(connection);

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            IEnumerable<string> pending =
                await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
            Assert.Empty(pending);

            // Stichprobe: die zuletzt ergänzte Tabelle existiert wirklich im migrierten Schema.
            Assert.Empty(await context.WatchedTitles.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BackfillWatchedForFavorites_SetsWatchedOnExistingFavorites()
        {
            using SqliteConnection connection = OpenConnection();
            using EchoPlayDbContext context = CreateContext(connection);

            // Auf den Stand vor der Backfill-Migration bringen und Altbestand einspielen:
            // favorisiert, aber unbeobachtet – genau der Zustand, den die Migration reparieren soll.
            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeBackfill, TestContext.Current.CancellationToken);

            await ExecuteAsync(connection,
                "INSERT INTO Series (Id, Title, IsSubscribed, IsFavorite, IsWatched, IsOnlineImported, IsCompleted, CreatedAt, IsDeleted) " +
                "VALUES ('11111111-1111-1111-1111-111111111111', 'TKKG', 1, 1, 0, 0, 0, '2026-01-01', 0);");
            await ExecuteAsync(connection,
                "INSERT INTO Series (Id, Title, IsSubscribed, IsFavorite, IsWatched, IsOnlineImported, IsCompleted, CreatedAt, IsDeleted) " +
                "VALUES ('22222222-2222-2222-2222-222222222222', 'Fünf Freunde', 1, 0, 0, 0, 0, '2026-01-01', 0);");

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM Series WHERE IsFavorite = 1 AND IsWatched = 1;"));

            // Nicht favorisierte Serien bleiben unangetastet – die Migration darf nicht pauschal überwachen.
            Assert.Equal(0, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM Series WHERE IsFavorite = 0 AND IsWatched = 1;"));
        }

        [Fact]
        public async Task DoppelteFolgenEntfernen_BehaeltEineFolgeUndRettetDenWiedergabestand()
        {
            using SqliteConnection connection = OpenConnection();
            using EchoPlayDbContext context = CreateContext(connection);

            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeDuplicateCleanup, TestContext.Current.CancellationToken);

            await ExecuteAsync(connection,
                "INSERT INTO Series (Id, Title, IsSubscribed, IsFavorite, IsWatched, IsOnlineImported, IsCompleted, CreatedAt, IsDeleted) " +
                "VALUES ('33333333-3333-3333-3333-333333333333', 'Die drei ???', 1, 0, 0, 1, 0, '2026-01-01', 0);");

            // Zwei Zeilen derselben Folge – der Zustand nach einem Re-Import auf gefülltem Bestand.
            await ExecuteAsync(connection,
                "INSERT INTO Episodes (Id, SeriesId, Title, AppleMusicAlbumId, Duration, TrackMatchKind, CreatedAt, IsDeleted) " +
                "VALUES ('aaaaaaa1-0000-0000-0000-000000000001', '33333333-3333-3333-3333-333333333333', 'Folge 230', 'am230', '00:00:00', 0, '2026-07-23 06:26:40', 0);");
            await ExecuteAsync(connection,
                "INSERT INTO Episodes (Id, SeriesId, Title, AppleMusicAlbumId, Duration, TrackMatchKind, CreatedAt, IsDeleted) " +
                "VALUES ('aaaaaaa2-0000-0000-0000-000000000002', '33333333-3333-3333-3333-333333333333', 'Folge 230', 'am230', '00:00:00', 0, '2026-07-23 06:30:27', 0);");

            // Gehört wurde nur die jüngere Zeile – die Markierung muss den Umzug überleben.
            await ExecuteAsync(connection,
                "INSERT INTO PlaybackStates (Id, EpisodeId, LastPosition, IsCompleted, CreatedAt, IsDeleted) " +
                "VALUES ('bbbbbbb1-0000-0000-0000-000000000001', 'aaaaaaa2-0000-0000-0000-000000000002', '00:12:00', 1, '2026-08-01', 0);");

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM Episodes;"));

            // Behalten wird die ältere Zeile.
            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM Episodes WHERE Id = 'aaaaaaa1-0000-0000-0000-000000000001';"));

            // Der Wiedergabestand hängt jetzt an ihr und ist weiterhin als gehört markiert.
            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM PlaybackStates WHERE EpisodeId = 'aaaaaaa1-0000-0000-0000-000000000001' AND IsCompleted = 1;"));
        }

        [Fact]
        public async Task DoppelteFolgenEntfernen_LaesstFolgenOhneAnbieterkennungUnangetastet()
        {
            using SqliteConnection connection = OpenConnection();
            using EchoPlayDbContext context = CreateContext(connection);

            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeDuplicateCleanup, TestContext.Current.CancellationToken);

            await ExecuteAsync(connection,
                "INSERT INTO Series (Id, Title, IsSubscribed, IsFavorite, IsWatched, IsOnlineImported, IsCompleted, CreatedAt, IsDeleted) " +
                "VALUES ('44444444-4444-4444-4444-444444444444', 'Pumuckel', 1, 0, 0, 0, 0, '2026-01-01', 0);");

            // Lokal eingelesene Folgen tragen keine Album-Kennung. Gleiche Titel in verschiedenen
            // Ordnern sind dort normal und dürfen nicht als Dubletten gelten.
            await ExecuteAsync(connection,
                "INSERT INTO Episodes (Id, SeriesId, Title, LocalFolderPath, Duration, TrackMatchKind, CreatedAt, IsDeleted) " +
                "VALUES ('ccccccc1-0000-0000-0000-000000000001', '44444444-4444-4444-4444-444444444444', 'Folge 1', 'D:\\A', '00:00:00', 0, '2026-01-01', 0);");
            await ExecuteAsync(connection,
                "INSERT INTO Episodes (Id, SeriesId, Title, LocalFolderPath, Duration, TrackMatchKind, CreatedAt, IsDeleted) " +
                "VALUES ('ccccccc2-0000-0000-0000-000000000002', '44444444-4444-4444-4444-444444444444', 'Folge 1', 'D:\\B', '00:00:00', 0, '2026-01-02', 0);");

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM Episodes;"));
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test-Helfer mit ausschließlich literalem SQL aus dieser Datei – keine Eingaben von außen.")]
        private static async Task ExecuteAsync(SqliteConnection connection, string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test-Helfer mit ausschließlich literalem SQL aus dieser Datei – keine Eingaben von außen.")]
        private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
