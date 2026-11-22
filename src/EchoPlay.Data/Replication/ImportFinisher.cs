using System.Globalization;
using System.Reflection;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Was der Abschluss eines Einspielens getan hat.
    /// </summary>
    /// <param name="RestoredSettings">Zurückgeschriebene gerätegebundene Einstellungen.</param>
    /// <param name="RestoredSecrets">Zurückgeschriebene Zugangsdaten dieses Rechners.</param>
    /// <param name="Rebased">Auf den Stammpfad dieses Rechners umgerechnete Pfade.</param>
    /// <param name="OutsideRoot">Pfade außerhalb des Stammpfads im Paket, unverändert.</param>
    public sealed record ImportFinished(int RestoredSettings, int RestoredSecrets, int Rebased, int OutsideRoot);

    /// <summary>
    /// Schließt ein Einspielen nach der Migration ab: gerätegebundene Einstellungen und Zugangsdaten
    /// dieses Rechners zurück, Pfade auf seinen Stammpfad.
    /// </summary>
    /// <remarks>
    /// Hatte der Rechner keinen Stammpfad, etwa bei einer frischen Installation, bleibt der aus dem
    /// Paket mitsamt seinen Pfaden stehen; den Stamm später zu ändern verschiebt sie dann. Sonst gilt
    /// der Stamm dieses Rechners, und alle Pfade aus dem Paket werden darauf umgerechnet.
    /// </remarks>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="rebaser">Verschiebt die Pfade.</param>
    /// <param name="secureSettings">Legt die Zugangsdaten ab.</param>
    /// <param name="loggerFactory">Die Logger-Fabrik.</param>
    public sealed class ImportFinisher(
        EchoPlayDbContext context,
        ILibraryRootRebaser rebaser,
        ISecureSettingsDataService secureSettings,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
    {
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("ImportFinisher");

        /// <summary>
        /// Schreibt das Erhaltene zurück und rechnet die Pfade um.
        /// </summary>
        /// <param name="carryOver">Was vor dem Ersetzen gelesen wurde.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Was getan wurde.</returns>
        public async Task<ImportFinished> FinishAsync(ImportCarryOver carryOver, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(carryOver);

            (int restoredSettings, string? deviceRoot) = await RestoreSettingsAsync(carryOver, cancellationToken).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            foreach (KeyValuePair<string, byte[]> secret in carryOver.Secrets)
            {
                await secureSettings.SaveAsync(secret.Key, secret.Value, cancellationToken).ConfigureAwait(false);
            }

            LibraryRebaseResult rebase = await RebaseIfMovedAsync(carryOver.Marker.PackageLibraryRoot, deviceRoot, cancellationToken).ConfigureAwait(false);

            ImportFinished finished = new(restoredSettings, carryOver.Secrets.Count, rebase.Rebased, rebase.OutsideRoot);
            _logger.Info(
                "Paket eingespielt (Absender {Device}): {Settings} Einstellungen und {Secrets} Zugangsdaten dieses Rechners erhalten, {Rebased} Pfade umgerechnet, {Outside} außerhalb des Stamms. Sicherung: {Backup}",
                carryOver.Marker.SourceDevice.ToString("N"), finished.RestoredSettings, finished.RestoredSecrets,
                finished.Rebased, finished.OutsideRoot, carryOver.BackupPath is null ? "keine" : Path.GetFileName(carryOver.BackupPath));
            return finished;
        }

        /// <summary>
        /// Schreibt die Einstellungen dieses Rechners zurück und speichert.
        /// </summary>
        /// <returns>Wie viele Werte zurückgeschrieben wurden und der Stammpfad danach.</returns>
        private async Task<(int Restored, string? DeviceRoot)> RestoreSettingsAsync(ImportCarryOver carryOver, CancellationToken cancellationToken)
        {
            AppSettings? settings = await context.AppSettings.AsTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                return (0, null);
            }

            int restored = carryOver.HadSettings ? Restore(settings, carryOver.DeviceSettings) : 0;

            // Mit der Mediathek ausgepackt: Der Zielordner ist jetzt der Stammpfad, auch gegen den
            // bisherigen dieses Rechners.
            if (!string.IsNullOrWhiteSpace(carryOver.Marker.LibraryRootOverride))
            {
                settings.LocalLibraryRootPath = carryOver.Marker.LibraryRootOverride;
            }

            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (restored, settings.LocalLibraryRootPath);
        }

        /// <summary>
        /// Rechnet die Pfade um, wenn der Stammpfad im Paket ein anderer ist als auf diesem Rechner.
        /// </summary>
        private async Task<LibraryRebaseResult> RebaseIfMovedAsync(string? packageRoot, string? deviceRoot, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageRoot) || string.IsNullOrWhiteSpace(deviceRoot)
                || string.Equals(Path.GetFullPath(packageRoot), Path.GetFullPath(deviceRoot), StringComparison.OrdinalIgnoreCase))
            {
                return new LibraryRebaseResult(0, 0);
            }

            return await rebaser.RebaseAsync(packageRoot, deviceRoot, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Schreibt die gerätegebundenen Werte zurück. Ein leerer Stammpfad bleibt draußen: Dann gilt
        /// der aus dem Paket.
        /// </summary>
        private static int Restore(AppSettings settings, IReadOnlyDictionary<string, object?> values)
        {
            int restored = 0;
            foreach (PropertyInfo property in PendingImportApplier.DeviceBoundSettings)
            {
                if (!values.TryGetValue(property.Name, out object? raw))
                {
                    continue;
                }

                if (property.Name == nameof(AppSettings.LocalLibraryRootPath) && string.IsNullOrWhiteSpace(raw as string))
                {
                    continue;
                }

                if (TryConvert(raw, property.PropertyType, out object? value))
                {
                    property.SetValue(settings, value);
                    restored++;
                }
            }

            return restored;
        }

        /// <summary>
        /// Wandelt einen Wert aus SQLite in den Typ der Eigenschaft. SQLite kennt nur Ganzzahl,
        /// Gleitkomma, Text und Blob; EF Core legt Wahrheitswerte und Aufzählungen als Ganzzahl und
        /// Zeitpunkte als Text ab.
        /// </summary>
        internal static bool TryConvert(object? raw, Type type, out object? value)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            value = null;
            if (raw is null)
            {
                return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
            }

            try
            {
                value = ConvertTo(raw, target);
                return value is not null;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>Die eigentliche Umwandlung; <see langword="null"/> für einen fremden Zieltyp.</summary>
        private static object? ConvertTo(object raw, Type target) => target switch
        {
            _ when target == typeof(string) => Convert.ToString(raw, CultureInfo.InvariantCulture),
            _ when target == typeof(bool) => Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0,
            _ when target == typeof(int) => Convert.ToInt32(raw, CultureInfo.InvariantCulture),
            _ when target == typeof(double) => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
            _ when target == typeof(DateTime) => DateTime.SpecifyKind(
                DateTime.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                DateTimeKind.Utc),
            _ when target.IsEnum => Enum.ToObject(target, Convert.ToInt64(raw, CultureInfo.InvariantCulture)),
            _ => null,
        };
    }
}
