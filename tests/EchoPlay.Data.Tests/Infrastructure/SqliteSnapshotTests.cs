using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Prüft <see cref="SqliteSnapshot"/>: Die Kopie enthält auch Änderungen, die noch im
    /// WAL-Journal stehen, und kein Dateiname kann die Kopie aus dem Verzeichnis der Quelle lenken.
    /// </summary>
    /// <remarks>
    /// Jeder Test bekommt ein eigenes Verzeichnis, weil xUnit die Tests einer Klasse parallel
    /// ausführt und SQLite-Dateien sich sonst gegenseitig sperren.
    /// </remarks>
    public sealed class SqliteSnapshotTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _databasePath;

        /// <summary>
        /// Legt je Test ein eigenes Verzeichnis mit einer leeren Datenbankdatei an.
        /// </summary>
        public SqliteSnapshotTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EchoPlaySnapshotTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
            _databasePath = Path.Combine(_directory, "source.db");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public async Task CreateAsync_CopiesRowsStillInWalJournal()
        {
            await using SqliteConnection source = new($"Data Source={_databasePath};Pooling=False");
            await source.OpenAsync(TestContext.Current.CancellationToken);
            using (SqliteCommand prepare = source.CreateCommand())
            {
                // Kein Checkpoint: Die Zeile steht danach nur im Journal, eine reine Dateikopie der
                // Hauptdatei enthielte sie nicht.
                prepare.CommandText = "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0; CREATE TABLE Items (Name TEXT); INSERT INTO Items VALUES ('drin');";
                _ = await prepare.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            string snapshotPath = await SqliteSnapshot.CreateAsync(_databasePath, "copy.db", TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(_directory, "copy.db"), snapshotPath);
            await using SqliteConnection copy = new($"Data Source={snapshotPath};Mode=ReadOnly;Pooling=False");
            await copy.OpenAsync(TestContext.Current.CancellationToken);
            using SqliteCommand read = copy.CreateCommand();
            read.CommandText = "SELECT Name FROM Items";
            Assert.Equal("drin", (string?)await read.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(@"..\escape.db")]
        [InlineData(@"sub\copy.db")]
        [InlineData(@"C:\copy.db")]
        [InlineData("..")]
        [InlineData(".")]
        [InlineData("co?py.db")]
        public async Task CreateAsync_RejectsNamesThatLeaveTheSourceDirectory(string snapshotFileName)
        {
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => SqliteSnapshot.CreateAsync(_databasePath, snapshotFileName, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CreateAsync_EmbedsApostropheInPathSafely()
        {
            await using (SqliteConnection source = new($"Data Source={_databasePath};Pooling=False"))
            {
                await source.OpenAsync(TestContext.Current.CancellationToken);
                using SqliteCommand prepare = source.CreateCommand();
                prepare.CommandText = "CREATE TABLE Items (Name TEXT);";
                _ = await prepare.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // Ein Hochkomma im Namen beendete ohne Verdoppeln das SQL-Literal vorzeitig.
            string snapshotPath = await SqliteSnapshot.CreateAsync(_databasePath, "o'brien.db", TestContext.Current.CancellationToken);

            Assert.True(File.Exists(snapshotPath));
        }
    }
}
