using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung des Datenservices für Anwendungseinstellungen.
    /// Die Einstellungen werden als einzelne Zeile in der Tabelle "AppSettings" gespeichert.
    /// </summary>
    /// <remarks>Initialisiert eine neue Instanz des <see cref="AppSettingsDataService"/>.</remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class AppSettingsDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IAppSettingsDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger =
            loggerFactory.CreateLogger("AppSettingsDataService");

        /// <summary>
        /// Lädt die Anwendungseinstellungen aus der Datenbank.
        /// Falls noch keine Einstellungen vorhanden sind, wird automatisch eine Standardinstanz erstellt und gespeichert.
        /// Gibt immer einen gültigen Wert zurück – niemals null.
        /// </summary>
        /// <returns>Die aktuellen <see cref="AppSettings"/>.</returns>
        public async Task<AppSettings> GetAsync()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("DB:AppSettings:Get");

            AppSettings? settings = await _context.AppSettings
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (settings is not null)
            {
                _logger.Debug("Einstellungen geladen.");
                return settings;
            }

            // Beim ersten Start gibt es noch keine Einstellungen – Defaults anlegen und speichern.
            _logger.Info("Keine Einstellungen gefunden – Standardkonfiguration wird erstellt.");

            AppSettings defaultSettings = new();
            _ = _context.AppSettings.Add(defaultSettings);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info("Standardkonfiguration gespeichert.");

            return defaultSettings;
        }

        /// <summary>
        /// Speichert die übergebenen Einstellungen dauerhaft.
        /// Die Entität muss aus dem aktuellen DbContext stammen oder bereits getrackt sein.
        /// </summary>
        /// <param name="settings">Die zu persistierenden Einstellungen.</param>
        public async Task SaveAsync(AppSettings settings)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("DB:AppSettings:Save");

            _ = _context.AppSettings.Update(settings);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info("Einstellungen gespeichert.");
        }
    }
}
