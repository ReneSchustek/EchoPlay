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
