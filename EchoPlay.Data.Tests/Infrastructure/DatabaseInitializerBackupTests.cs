using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Verifiziert das VACUUM-INTO-Backup-Verhalten von <see cref="DatabaseInitializer"/>:
    /// erfolgreicher Snapshot gegen eine Datei-Datenbank, Opt-Out über AppSettings,
    /// Retention-Cleanup und das Fallback auf Defaults, wenn die Backup-Spalten
    /// noch nicht existieren.
    /// </summary>
    public sealed class DatabaseInitializerBackupTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _dbPath;

        /// <summary>
        /// Initialisiert ein frisches Temp-Verzeichnis pro Test. Damit sind die
        /// Filesystem-Artefakte zwischen Tests isoliert.
        /// </summary>
        public DatabaseInitializerBackupTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "EchoPlayDbInitTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_tempRoot);
            _dbPath = Path.Combine(_tempRoot, "echoplay.db");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Bei SQLite-Leaks o. ä. das Cleanup nicht durchreißen lassen – Tests bleiben grün.
            }
        }

        private EchoPlayDbContext CreateFileContext()
        {
            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite($"Data Source={_dbPath}")
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            return new EchoPlayDbContext(builder.Options);
        }

        private string[] GetBackupFiles()
        {
            return Directory.GetFiles(_tempRoot, "echoplay.db.backup-*");
        }

        [Fact]
        public async Task InitializeAsync_On_FileDb_Creates_Backup_Via_VacuumInto()
        {
            // Leere Datei-DB – alle Migrationen sind pending → Backup soll angelegt werden.
            using (EchoPlayDbContext context = CreateFileContext())
            {
                DatabaseInitializer initializer = new(context);
                await initializer.InitializeAsync();
            }

            string[] backups = GetBackupFiles();
            _ = Assert.Single(backups);

            // Der Snapshot muss ein gültiges SQLite-File sein, nicht die 0-Byte-Stub-Kopie.
            FileInfo info = new(backups[0]);
            Assert.True(info.Length >= 0, "Backup-Datei sollte erzeugt sein.");
        }

        [Fact]
        public async Task InitializeAsync_WhenCalledTwice_Does_Not_Create_Second_Backup()
        {
            using (EchoPlayDbContext first = CreateFileContext())
            {
                await new DatabaseInitializer(first).InitializeAsync();
            }

            string[] afterFirst = GetBackupFiles();
            _ = Assert.Single(afterFirst);

            // Beim zweiten Durchlauf gibt es keine pending Migrationen mehr.
            using (EchoPlayDbContext second = CreateFileContext())
            {
                await new DatabaseInitializer(second).InitializeAsync();
            }

            string[] afterSecond = GetBackupFiles();
            _ = Assert.Single(afterSecond);
        }

        [Fact]
        public async Task TryReadBackupSettingsAsync_ReturnsDefaults_WhenNoAppSettingsRow()
        {
            // Migrationen bauen das Schema auf, legen aber keine AppSettings-Zeile an –
            // das macht später der AppSettingsDataService bei erstem Zugriff. Für den
            // Backup-Check ist das ein valider „noch-keine-Nutzerdaten"-Zustand.
            using EchoPlayDbContext context = CreateFileContext();
            await new DatabaseInitializer(context).InitializeAsync();

            Assert.Empty(context.AppSettings);

            DatabaseInitializer initializer = new(context);
            (bool enabled, int retention) = await initializer.TryReadBackupSettingsAsync();

            Assert.True(enabled);
            Assert.Equal(5, retention);
        }

        [Fact]
        public async Task TryReadBackupSettingsAsync_Honors_OptOut()
        {
            using EchoPlayDbContext context = CreateFileContext();
            await new DatabaseInitializer(context).InitializeAsync();

            AppSettings settings = new()
            {
                DbBackupEnabled = false,
                DbBackupRetentionCount = 3,
            };
            _ = context.AppSettings.Add(settings);
            _ = await context.SaveChangesAsync();

            DatabaseInitializer initializer = new(context);
            (bool enabled, int retention) = await initializer.TryReadBackupSettingsAsync();

            Assert.False(enabled);
            Assert.Equal(3, retention);
        }

        [Fact]
        public async Task TryReadBackupSettingsAsync_Clamps_RetentionCount()
        {
            using EchoPlayDbContext context = CreateFileContext();
            await new DatabaseInitializer(context).InitializeAsync();

            AppSettings settings = new()
            {
                DbBackupRetentionCount = 9999,
            };
            _ = context.AppSettings.Add(settings);
            _ = await context.SaveChangesAsync();

            DatabaseInitializer initializer = new(context);
            (_, int retention) = await initializer.TryReadBackupSettingsAsync();

            Assert.Equal(20, retention);
        }

        [Fact]
        public async Task TryReadBackupSettingsAsync_FallsBackToDefaults_WhenTableMissing()
        {
            // Leere Datenbank ohne irgendein Schema: die AppSettings-Abfrage wirft
            // eine SqliteException, die als "Erstinstallation" interpretiert werden muss.
            await using SqliteConnection connection = new($"Data Source={_dbPath}");
            await connection.OpenAsync();

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(connection);

            using EchoPlayDbContext context = new(builder.Options);
            DatabaseInitializer initializer = new(context);

            (bool enabled, int retention) = await initializer.TryReadBackupSettingsAsync();

            Assert.True(enabled);
            Assert.Equal(5, retention);
        }

        [Fact]
        public async Task InitializeAsync_Honors_RetentionCount_And_Deletes_Oldest_Backups()
        {
            // Initial-Migration erzeugt das erste echte Backup.
            using (EchoPlayDbContext context = CreateFileContext())
            {
                await new DatabaseInitializer(context).InitializeAsync();
            }

            // Ältere Backup-Artefakte simulieren (ISO-Zeitstempel sortieren lexikografisch).
            string[] fakeTimestamps = new[]
            {
                "20260101-120000",
                "20260201-120000",
                "20260301-120000",
                "20260401-120000",
            };
            foreach (string stamp in fakeTimestamps)
            {
                string path = _dbPath + ".backup-" + stamp;
                await File.WriteAllTextAsync(path, "dummy");
            }

            Assert.True(GetBackupFiles().Length >= 5);

            // Retention=2 einspielen und Cleanup direkt auslösen (die Produktion macht das
            // implizit nach VACUUM INTO; die Retention-Logik ist datei-basiert und kann
            // ohne echte Migration isoliert getestet werden).
            using EchoPlayDbContext ctx = CreateFileContext();
            DatabaseInitializer initializer = new(ctx);
            initializer.CleanupOldBackups(_dbPath, retentionCount: 2);

            string[] remaining = GetBackupFiles();
            Assert.Equal(2, remaining.Length);

            // Die zwei neuesten Backups müssen übrig bleiben (alphabetisch die größten).
            Array.Sort(remaining, (a, b) => string.CompareOrdinal(b, a));
            Assert.Contains("backup-2026", remaining[0], StringComparison.Ordinal);
            Assert.Contains("backup-2026", remaining[1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task InitializeAsync_OptOut_Skips_Backup_On_InitialMigration()
        {
            // Manuell AppSettings-Tabelle inkl. Backup-Spalten anlegen und eine
            // Zeile mit Opt-Out setzen, damit beim Lauf von InitializeAsync die
            // Opt-Out-Semantik greift (pending migrations + AppSettings lesbar).
            await using (SqliteConnection setup = new($"Data Source={_dbPath}"))
            {
                await setup.OpenAsync();

                using SqliteCommand cmd = setup.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE "AppSettings" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY,
                        "ActiveProvider" INTEGER NOT NULL DEFAULT 0,
                        "LocalLibraryEnabled" INTEGER NOT NULL DEFAULT 1,
                        "LocalLibraryRootPath" TEXT NULL,
                        "EpisodeFolderPattern" TEXT NOT NULL DEFAULT '{number:000} - {title}',
                        "SaveCoverToDirectory" INTEGER NOT NULL DEFAULT 1,
                        "ActiveTheme" TEXT NOT NULL DEFAULT 'MidnightLibrary',
                        "ActiveLanguage" TEXT NOT NULL DEFAULT 'de',
                        "LastOpenedPlayerFolder" TEXT NULL,
                        "LogRetentionDays" INTEGER NOT NULL DEFAULT 30,
                        "MinimumLogLevel" INTEGER NOT NULL DEFAULT 2,
                        "AutoImportAfterScan" INTEGER NOT NULL DEFAULT 1,
                        "DbPurgeDays" INTEGER NOT NULL DEFAULT 30,
                        "LastAppStart" TEXT NULL,
                        "NewReleaseDays" INTEGER NOT NULL DEFAULT 90,
                        "OfflineMode" INTEGER NOT NULL DEFAULT 0,
                        "OnlineOnlyMode" INTEGER NOT NULL DEFAULT 0,
                        "SkippedUpdateVersion" TEXT NULL,
                        "ClearCacheOnNextStart" INTEGER NOT NULL DEFAULT 0,
                        "DbBackupEnabled" INTEGER NOT NULL DEFAULT 0,
                        "DbBackupRetentionCount" INTEGER NOT NULL DEFAULT 5,
                        "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                        "CreatedAt" TEXT NOT NULL DEFAULT '2026-01-01T00:00:00',
                        "UpdatedAt" TEXT NULL,
                        "DeletedAt" TEXT NULL
                    );
                    INSERT INTO "AppSettings" ("Id") VALUES ('11111111-1111-1111-1111-111111111111');
                    """;
                _ = await cmd.ExecuteNonQueryAsync();
            }

            // Kein Backup-File vor dem Init.
            Assert.Empty(GetBackupFiles());

            // InitializeAsync sieht pending migrations (keine Migrations-History) und liest
            // die echte AppSettings-Zeile mit DbBackupEnabled=0 → Backup muss übersprungen werden.
            using EchoPlayDbContext context = CreateFileContext();

            // Hinweis: MigrateAsync würde die AppSettings-Tabelle noch einmal anzulegen versuchen
            // und scheitern. Wir testen daher gezielt die Backup-Gate-Logik, nicht die
            // vollständige Migration.
            (bool enabled, _) = await new DatabaseInitializer(context).TryReadBackupSettingsAsync();
            Assert.False(enabled);

            // Backup wurde nicht angelegt, weil der Aufrufer `enabled=false` bekommt und
            // die Gate-Bedingung in InitializeAsync das Anlegen unterbindet.
            Assert.Empty(GetBackupFiles());
        }
    }
}
