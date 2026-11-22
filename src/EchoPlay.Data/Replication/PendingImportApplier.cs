using System.Globalization;
using System.Reflection;
using EchoPlay.Data.Entities.Contracts;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Was vom bisherigen Bestand über das Einspielen hinweg erhalten bleibt.
    /// </summary>
    /// <param name="Marker">Die Markierung des Einspielens.</param>
    /// <param name="HadSettings">Ob der bisherige Bestand eine Einstellungszeile hatte.</param>
    /// <param name="DeviceSettings">Die gerätegebundenen Einstellungen, nach Eigenschaftsname.</param>
    /// <param name="Secrets">Die per DPAPI verschlüsselten Zugangsdaten dieses Rechners.</param>
    /// <param name="BackupPath">Die Sicherung des bisherigen Bestands, falls einer da war.</param>
    public sealed record ImportCarryOver(
        PendingImport Marker,
        bool HadSettings,
        IReadOnlyDictionary<string, object?> DeviceSettings,
        IReadOnlyList<KeyValuePair<string, byte[]>> Secrets,
        string? BackupPath);

    /// <summary>
    /// Spielt ein vorbereitetes Paket beim Start ein, bevor irgendetwas die Datenbank öffnet.
    /// </summary>
    /// <remarks>
    /// Ablauf: Gerätegebundenes aus dem bisherigen Bestand lesen, den Bestand per <c>VACUUM INTO</c>
    /// sichern, die Datei durch die entpackte ersetzen. Danach läuft die gewöhnliche Migration auf den
    /// aktuellen Stand, und <see cref="ImportFinisher"/> schreibt das Gerätegebundene zurück. So
    /// behält der Rechner seinen Stammpfad, seine Lautstärke und seine eigenen Zugangsdaten, auch
    /// wenn das Paket von einem anderen kommt; welche Einstellung dazu zählt, sagt
    /// <see cref="DataScope.DeviceBound"/>.
    /// <para>
    /// Gelesen wird mit <c>SELECT *</c> und dann je Spalte nach Namen. Die bisherige Datei kann
    /// älter sein als das Modell, und eine Abfrage über die Spalten des Modells scheiterte an der
    /// ersten, die es dort noch nicht gibt.
    /// </para>
    /// </remarks>
    public static class PendingImportApplier
    {
        private const string ReadSettings = """SELECT * FROM "AppSettings" LIMIT 1;""";
        private const string ReadSecrets = """SELECT "Key", "EncryptedValue" FROM "SecureSettings" WHERE NOT "IsDeleted";""";

        /// <summary>Die Namen der gerätegebundenen Einstellungen, aus der Einteilung am Feld.</summary>
        internal static IReadOnlyList<PropertyInfo> DeviceBoundSettings { get; } = [.. typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<DataScopeAttribute>()?.Scope == DataScope.DeviceBound)];

        /// <summary>
        /// Ersetzt die Datenbank, wenn ein Einspielen ansteht.
        /// </summary>
        /// <param name="databasePath">Die Datei der Datenbank.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Was erhalten bleibt, oder <see langword="null"/>, wenn nichts anstand.</returns>
        public static async Task<ImportCarryOver?> ApplyAsync(string databasePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;

            PendingImport? marker = PendingImport.TryRead(directory);
            string staged = Path.Combine(directory, PackageStager.StagedFileName);
            if (marker is null || !File.Exists(staged))
            {
                if (marker is not null || File.Exists(staged))
                {
                    PendingImport.Discard(directory);
                }

                return null;
            }

            bool hadDatabase = File.Exists(databasePath) && new FileInfo(databasePath).Length > 0;
            Dictionary<string, object?> settings = [];
            List<KeyValuePair<string, byte[]>> secrets = [];
            bool hadSettings = false;
            string? backup = null;

            if (hadDatabase)
            {
                hadSettings = await ReadCarryOverAsync(databasePath, settings, secrets, cancellationToken).ConfigureAwait(false);
                backup = await SqliteSnapshot.CreateAsync(
                    databasePath,
                    $"echoplay.db.before-import-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}",
                    cancellationToken).ConfigureAwait(false);
            }

            ReplaceDatabase(databasePath, staged, directory);
            return new ImportCarryOver(marker, hadSettings, settings, secrets, backup);
        }

        /// <summary>
        /// Legt die vorbereitete Datei an die Stelle der Datenbank. Die Begleitdateien des
        /// Protokolls gehören zur alten und müssen mit; zuletzt fällt der Merker weg.
        /// </summary>
        private static void ReplaceDatabase(string databasePath, string staged, string directory)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
            File.Move(staged, databasePath, overwrite: true);
            File.Delete(Path.Combine(directory, PendingImport.FileName));
        }

        private static async Task<bool> ReadCarryOverAsync(
            string databasePath,
            Dictionary<string, object?> settings,
            List<KeyValuePair<string, byte[]>> secrets,
            CancellationToken cancellationToken)
        {
            SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                bool hadSettings = await ReadSettingsAsync(connection, settings, cancellationToken).ConfigureAwait(false);
                await ReadSecretsAsync(connection, secrets, cancellationToken).ConfigureAwait(false);
                return hadSettings;
            }
        }

        private static async Task<bool> ReadSettingsAsync(SqliteConnection connection, Dictionary<string, object?> settings, CancellationToken cancellationToken)
        {
            SqliteCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ReadSettings;
                SqliteDataReader reader;
                try
                {
                    reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Keine Einstellungstabelle: ein Bestand vor der ersten Migration. Nichts zu erhalten.
                    return false;
                }

                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }

                    HashSet<string> columns = [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)];
                    foreach (PropertyInfo property in DeviceBoundSettings.Where(p => columns.Contains(p.Name)))
                    {
                        object raw = reader.GetValue(reader.GetOrdinal(property.Name));
                        settings[property.Name] = raw is DBNull ? null : raw;
                    }

                    return true;
                }
            }
        }

        private static async Task ReadSecretsAsync(SqliteConnection connection, List<KeyValuePair<string, byte[]>> secrets, CancellationToken cancellationToken)
        {
            SqliteCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ReadSecrets;
                SqliteDataReader reader;
                try
                {
                    reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    return;
                }

                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        secrets.Add(new(reader.GetString(0), (byte[])reader.GetValue(1)));
                    }
                }
            }
        }
    }
}
