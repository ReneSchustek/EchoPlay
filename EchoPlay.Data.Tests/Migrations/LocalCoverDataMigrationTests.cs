using EchoPlay.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Globalization;

namespace EchoPlay.Data.Tests.Migrations
{
    /// <summary>
    /// Regressions-Tests für die Migration <c>MigrateLocalCoverDataToCoverImages</c>.
    /// Verifiziert, dass bestehende <c>LocalCoverData</c>-BLOBs verlustfrei in die
    /// <c>CoverImages</c>-Tabelle übernommen werden, bevor die Spalten entfernt werden.
    /// </summary>
    public sealed class LocalCoverDataMigrationTests : IAsyncLifetime, IDisposable
    {
        private const string PreviousMigration = "20260416121304_AddDbBackupSettings";
        private static readonly string FixedNow = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        private static readonly Guid FixedCoverId = new("11111111-2222-3333-4444-aaaaaaaaaaaa");

        private SqliteConnection? _connection;
        private EchoPlayDbContext? _context;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            await _connection.OpenAsync();

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(_connection)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            _context = new EchoPlayDbContext(builder.Options);

            // Schema bis zum Vorgaenger-Stand aufbauen,
            // damit die Spalten Series.LocalCoverData und Episodes.LocalCoverData noch existieren.
            IMigrator migrator = _context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
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
        public async Task Migration_DropsLocalCoverDataColumns_FromBothTables()
        {
            // Migration auf den neuesten Stand bringen
            await _context!.Database.MigrateAsync();

            using SqliteCommand cmd = _connection!.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Series') WHERE name = 'LocalCoverData';";
            long seriesColumns = (long)(await cmd.ExecuteScalarAsync())!;

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Episodes') WHERE name = 'LocalCoverData';";
            long episodeColumns = (long)(await cmd.ExecuteScalarAsync())!;

            Assert.Equal(0, seriesColumns);
            Assert.Equal(0, episodeColumns);
        }

        [Fact]
        public async Task Migration_CopiesSeriesLocalCoverData_IntoCoverImages()
        {
            Guid seriesId = new("11111111-1111-1111-1111-111111111111");
            byte[] coverBytes = [0xFF, 0xD8, 0xFF, 0xE0];

            await InsertSeriesWithLocalCoverAsync(seriesId, "TKKG", coverBytes);

            await _context!.Database.MigrateAsync();

            byte[]? migrated = await ReadCoverImageBytesAsync("Series", seriesId);

            Assert.NotNull(migrated);
            Assert.Equal(coverBytes, migrated);
        }

        [Fact]
        public async Task Migration_CopiesEpisodeLocalCoverData_IntoCoverImages()
        {
            Guid seriesId = new("22222222-2222-2222-2222-222222222222");
            Guid episodeId = new("33333333-3333-3333-3333-333333333333");
            byte[] coverBytes = [0x89, 0x50, 0x4E, 0x47];

            await InsertSeriesWithLocalCoverAsync(seriesId, "Die drei ???", coverData: null);
            await InsertEpisodeWithLocalCoverAsync(episodeId, seriesId, coverBytes);

            await _context!.Database.MigrateAsync();

            byte[]? migrated = await ReadCoverImageBytesAsync("Episode", episodeId);

            Assert.NotNull(migrated);
            Assert.Equal(coverBytes, migrated);
        }

        [Fact]
        public async Task Migration_DoesNotInsertDuplicate_WhenCoverImageAlreadyExists()
        {
            // Wenn bereits ein Eintrag in CoverImages für die Serie existiert,
            // darf die Nachzügler-Migration keinen zweiten Datensatz anlegen.
            Guid seriesId = new("44444444-4444-4444-4444-444444444444");
            byte[] originalBytes = [0x01, 0x02];
            byte[] legacyBytes = [0xFF, 0xFF, 0xFF];

            await InsertSeriesWithLocalCoverAsync(seriesId, "Bibi Blocksberg", legacyBytes);
            await InsertExistingCoverImageAsync("Series", seriesId, originalBytes);

            await _context!.Database.MigrateAsync();

            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM CoverImages WHERE EntityType = 'Series' AND EntityId = $id;";
            _ = cmd.Parameters.AddWithValue("$id", seriesId.ToString());
            long count = (long)(await cmd.ExecuteScalarAsync())!;

            Assert.Equal(1, count);

            byte[]? remaining = await ReadCoverImageBytesAsync("Series", seriesId);
            Assert.Equal(originalBytes, remaining);
        }

        private async Task InsertSeriesWithLocalCoverAsync(Guid id, string title, byte[]? coverData)
        {
            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Series (Id, Title, LocalCoverData, IsCompleted, IsOnlineImported, IsSubscribed,
                    IsFavorite, IsWatched, CreatedAt, IsDeleted)
                VALUES ($id, $title, $cover, 0, 0, 0, 0, 0, $now, 0);
                """;
            _ = cmd.Parameters.AddWithValue("$id", id.ToString());
            _ = cmd.Parameters.AddWithValue("$title", title);
            _ = cmd.Parameters.AddWithValue("$cover", (object?)coverData ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("$now", FixedNow);
            _ = await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertEpisodeWithLocalCoverAsync(Guid id, Guid seriesId, byte[] coverData)
        {
            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Episodes (Id, Title, Duration, SeriesId, LocalCoverData,
                    TrackMatchKind, CreatedAt, IsDeleted)
                VALUES ($id, $title, $duration, $seriesId, $cover, 0, $now, 0);
                """;
            _ = cmd.Parameters.AddWithValue("$id", id.ToString());
            _ = cmd.Parameters.AddWithValue("$title", "Folge 1");
            _ = cmd.Parameters.AddWithValue("$duration", "00:30:00");
            _ = cmd.Parameters.AddWithValue("$seriesId", seriesId.ToString());
            _ = cmd.Parameters.AddWithValue("$cover", coverData);
            _ = cmd.Parameters.AddWithValue("$now", FixedNow);
            _ = await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertExistingCoverImageAsync(string entityType, Guid entityId, byte[] imageData)
        {
            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CoverImages (Id, EntityType, EntityId, ImageData, CreatedAt, IsDeleted)
                VALUES ($id, $type, $entityId, $bytes, $now, 0);
                """;
            _ = cmd.Parameters.AddWithValue("$id", FixedCoverId.ToString());
            _ = cmd.Parameters.AddWithValue("$type", entityType);
            _ = cmd.Parameters.AddWithValue("$entityId", entityId.ToString());
            _ = cmd.Parameters.AddWithValue("$bytes", imageData);
            _ = cmd.Parameters.AddWithValue("$now", FixedNow);
            _ = await cmd.ExecuteNonQueryAsync();
        }

        private async Task<byte[]?> ReadCoverImageBytesAsync(string entityType, Guid entityId)
        {
            using SqliteCommand cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT ImageData FROM CoverImages WHERE EntityType = $type AND EntityId = $id;";
            _ = cmd.Parameters.AddWithValue("$type", entityType);
            _ = cmd.Parameters.AddWithValue("$id", entityId.ToString());
            object? result = await cmd.ExecuteScalarAsync();
            return result as byte[];
        }
    }
}
