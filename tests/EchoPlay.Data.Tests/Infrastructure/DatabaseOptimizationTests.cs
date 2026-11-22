using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Tests für Datenbank-Optimierungen:
    /// PRAGMA-Konfiguration, Indizes und Fremdschlüssel-Beziehungen.
    /// </summary>
    public sealed class DatabaseOptimizationTests : DbTestBase
    {
        [Fact]
        public void Migration_CreatesSubscribedTitleIndex_OnSeries()
        {
            // Der Kombi-Index (IsSubscribed, Title) muss für GetSubscribedAsync() existieren
            AssertIndexExists("IX_Series_IsSubscribed_Title");
        }

        [Fact]
        public void Migration_CreatesFavoriteTitleIndex_OnSeries()
        {
            // Der Kombi-Index (IsFavorite, Title) muss für die Dashboard-Favoriten existieren
            AssertIndexExists("IX_Series_IsFavorite_Title");
        }

        [Fact]
        public void Migration_CreatesSpotifyArtistIdIndex_OnSeries()
        {
            // Spotify-Lookup-Index für Duplikat-Erkennung beim Import
            AssertIndexExists("IX_Series_SpotifyArtistId");
        }

        [Fact]
        public void Migration_CreatesAppleMusicArtistIdIndex_OnSeries()
        {
            // Apple-Music-Lookup-Index für Duplikat-Erkennung beim Import
            AssertIndexExists("IX_Series_AppleMusicArtistId");
        }

        [Fact]
        public void Migration_CreatesPurgeIndex_OnSeries()
        {
            // Purge-Index (IsDeleted, DeletedAt) für DatabaseMaintenanceService
            AssertIndexExists("IX_Series_IsDeleted_DeletedAt");
        }

        [Fact]
        public void Migration_CreatesPurgeIndex_OnEpisodes()
        {
            AssertIndexExists("IX_Episodes_IsDeleted_DeletedAt");
        }

        [Fact]
        public void Migration_CreatesPurgeIndex_OnPlaybackStates()
        {
            AssertIndexExists("IX_PlaybackStates_IsDeleted_DeletedAt");
        }

        [Fact]
        public void Migration_CreatesPurgeIndex_OnLocalTracks()
        {
            AssertIndexExists("IX_LocalTracks_IsDeleted_DeletedAt");
        }

        [Fact]
        public void Migration_CreatesCompletedEpisodeIndex_OnPlaybackStates()
        {
            // Dashboard zählt gehörte Episoden über (IsCompleted, EpisodeId)
            AssertIndexExists("IX_PlaybackStates_IsCompleted_EpisodeId");
        }

        [Fact]
        public void Migration_CreatesEpisodeTrackNumberIndex_OnLocalTracks()
        {
            // Kombi-Index ersetzt den alten einfachen EpisodeId-Index
            AssertIndexExists("IX_LocalTracks_EpisodeId_TrackNumber");
        }

        [Fact]
        public void Migration_CreatesLocalFolderPathIndex_OnEpisodes()
        {
            // Lokal-Bibliothek: fehlende Episoden werden über (SeriesId, LocalFolderPath) gesucht
            AssertIndexExists("IX_Episodes_SeriesId_LocalFolderPath");
        }

        [Fact]
        public void Migration_CreatesSectionPositionIndex_OnDashboardPositions()
        {
            // GetBySectionAsync() filtert auf Section und sortiert nach Position
            AssertIndexExists("IX_DashboardPositions_Section_Position");
        }

        [Fact]
        public async Task DashboardPosition_RequiresExistingSeries()
        {
            // FK DashboardPosition → Series muss verhindern, dass Positionen
            // für nicht existierende Serien angelegt werden.
            DashboardPosition orphanPosition = new()
            {
                SeriesId = new Guid("99999999-9999-9999-9999-999999999994"),
                Section = "Favoriten",
                Position = 0
            };

            _ = Context.DashboardPositions.Add(orphanPosition);

            // SaveChanges muss fehlschlagen, weil die referenzierte Serie nicht existiert
            _ = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DashboardPosition_WorksWithExistingSeries()
        {
            // FK erlaubt Positionen für existierende Serien
            Series series = new() { Title = "TKKG" };
            _ = Context.Series.Add(series);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            DashboardPosition position = new()
            {
                SeriesId = series.Id,
                Section = "Favoriten",
                Position = 0
            };
            _ = Context.DashboardPositions.Add(position);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            DashboardPosition? loaded = await Context.DashboardPositions
                .FirstOrDefaultAsync(dp => dp.SeriesId == series.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(loaded);
            Assert.Equal("Favoriten", loaded.Section);
        }

        [Fact]
        public void PragmaInterceptor_SetsForeignKeysOn()
        {
            // Der Interceptor muss PRAGMA foreign_keys = ON setzen.
            // Die Tests legen das Schema mit EnsureCreated() an, deshalb wird der PRAGMA-Wert
            // über eine eigene Verbindung mit dem Interceptor geprüft.
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            // Interceptor manuell triggern, simuliert das EF-Core-Verbindungsverhalten
            SqlitePragmaInterceptor interceptor = new();
            interceptor.ConnectionOpened(connection, null!);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            long result = (long)command.ExecuteScalar()!;

            // 1 = ON, 0 = OFF
            Assert.Equal(1, result);

            connection.Close();
        }

        [Fact]
        public void PragmaInterceptor_SetsJournalModeWal()
        {
            // WAL gibt es nur für Datenbankdateien; eine In-Memory-Datenbank meldet immer
            // „memory" und könnte ein fehlendes PRAGMA nicht aufdecken.
            string directory = Path.Combine(Path.GetTempPath(), "EchoPlayPragmaTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(directory);
            try
            {
                // Die Verbindung endet mit dem try-Block, also vor dem Löschen im finally.
                using SqliteConnection connection = new($"Data Source={Path.Combine(directory, "wal.db")};Pooling=False");
                connection.Open();
                new SqlitePragmaInterceptor().ConnectionOpened(connection, null!);

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode;";
                Assert.Equal("wal", (string?)command.ExecuteScalar());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void PragmaInterceptor_SetsCacheSize()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();
            new SqlitePragmaInterceptor().ConnectionOpened(connection, null!);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA cache_size;";

            // Negativ heißt Kilobyte: 64 MB.
            Assert.Equal(-65536L, (long)command.ExecuteScalar()!);
        }

        [Fact]
        public void PragmaInterceptor_SetsSynchronousNormal()
        {
            // PRAGMA synchronous = NORMAL muss gesetzt sein
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            SqlitePragmaInterceptor interceptor = new();
            interceptor.ConnectionOpened(connection, null!);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous;";
            long result = (long)command.ExecuteScalar()!;

            // 1 = NORMAL (0 = OFF, 2 = FULL, 3 = EXTRA)
            Assert.Equal(1, result);

            connection.Close();
        }

        [Fact]
        public void PragmaInterceptor_SetsTempStoreMemory()
        {
            // PRAGMA temp_store = MEMORY (2) muss gesetzt sein
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            SqlitePragmaInterceptor interceptor = new();
            interceptor.ConnectionOpened(connection, null!);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA temp_store;";
            long result = (long)command.ExecuteScalar()!;

            // 2 = MEMORY (0 = DEFAULT, 1 = FILE)
            Assert.Equal(2, result);

            connection.Close();
        }

        [Fact]
        public void DbContext_UsesNoTrackingByDefault()
        {
            // Der globale Default muss NoTracking sein, damit Lese-Abfragen
            // nicht unnötig den Change-Tracker belasten.
            Assert.Equal(
                QueryTrackingBehavior.NoTracking,
                Context.ChangeTracker.QueryTrackingBehavior);
        }

        /// <summary>
        /// Prüft, ob ein Index mit dem angegebenen Namen in der SQLite-Datenbank existiert.
        /// SQLite speichert alle Index-Definitionen in der Systemtabelle <c>sqlite_master</c>.
        /// </summary>
        /// <param name="indexName">Der erwartete Index-Name.</param>
        private void AssertIndexExists(string indexName)
        {
            Microsoft.Data.Sqlite.SqliteConnection connection =
                (Microsoft.Data.Sqlite.SqliteConnection)Context.Database.GetDbConnection();

            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@indexName";
            _ = command.Parameters.AddWithValue("@indexName", indexName);
            long count = (long)command.ExecuteScalar()!;

            Assert.True(count > 0, $"Index '{indexName}' existiert nicht in der Datenbank.");
        }
    }
}
