using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung für benutzerdefinierte Dashboard-Positionen.
    /// Verwaltet die Sortierreihenfolge von Serien in Dashboard-Bereichen wie
    /// „Neuerscheinungen" und „Favoriten".
    /// </summary>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class DashboardPositionDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IDashboardPositionDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger =
            loggerFactory.CreateLogger("DashboardPositionDataService");

        /// <inheritdoc />
        public async Task<IReadOnlyList<DashboardPosition>> GetBySectionAsync(string section)
        {
            _logger.Debug($"Lade Dashboard-Positionen für Bereich '{section}'.");

            List<DashboardPosition> result = await _context.DashboardPositions

                .Where(dp => dp.Section == section)
                .OrderBy(dp => dp.Position)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} Position(en) für '{section}' geladen.");
            return result;
        }

        /// <inheritdoc />
        public async Task SaveOrderAsync(string section, IReadOnlyList<Guid> seriesIds)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            _logger.Info($"Speichere Reihenfolge für '{section}' ({seriesIds.Count} Serien).");

            // Bestehende Positionen des Bereichs entfernen – Replace-Strategie ist einfacher
            // und konsistenter als ein Merge, da die gesamte Reihenfolge auf einmal gesetzt wird.
            List<DashboardPosition> existing = await _context.DashboardPositions
                .AsTracking()
                .Where(dp => dp.Section == section)
                .ToListAsync().ConfigureAwait(false);

            _context.DashboardPositions.RemoveRange(existing);

            // Neue Positionen anlegen
            for (int i = 0; i < seriesIds.Count; i++)
            {
                DashboardPosition position = new()
                {
                    SeriesId = seriesIds[i],
                    Section = section,
                    Position = i
                };

                _ = _context.DashboardPositions.Add(position);
            }

            try
            {
                _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (UniqueConstraintHandler.IsUniqueViolation(ex))
            {
                // Paralleler Scope hat für dieselbe Section bereits Positionen geschrieben —
                // ignorieren, da die zweite Reihenfolge gleichwertig ist.
                _logger.Warning($"UNIQUE-Konflikt bei Dashboard-Positionen ignoriert: {ex.InnerException?.Message}");
                return;
            }

            _logger.Info($"Reihenfolge für '{section}' gespeichert ({seriesIds.Count} Positionen).");
        }
    }
}
