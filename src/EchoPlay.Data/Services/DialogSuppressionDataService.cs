using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Verwaltung der dauerhaft ausgeblendeten Dialoge.
    /// </summary>
    /// <remarks>
    /// Initialisiert eine neue Instanz des <see cref="DialogSuppressionDataService"/>.
    /// </remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class DialogSuppressionDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IDialogSuppressionDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("DialogSuppressionDataService");

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<DialogSuppression>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.DialogSuppressions
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="key">Name des <c>DialogKey</c>-Werts.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SuppressAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            bool exists = await _context.DialogSuppressions
                .AnyAsync(d => d.Key == key, cancellationToken).ConfigureAwait(false);

            if (exists)
            {
                return;
            }

            _ = _context.DialogSuppressions.Add(new DialogSuppression { Key = key });

            // Zwischen Prüfung und Einfügen kann dasselbe Häkchen in einem zweiten Dialog
            // gesetzt worden sein. Der UNIQUE-Index fängt das ab; der Konflikt ist hier
            // kein Fehler, sondern genau das gewünschte Ergebnis.
            DbUpdateException? conflict = await _context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                _logger.Debug(() => $"Dialog '{key}' war bereits ausgeblendet (paralleler Schreibzugriff).");
                return;
            }

            _logger.Info("Dialog '{Key}' dauerhaft ausgeblendet.", key);
        }

        /// <inheritdoc/>
        /// <param name="key">Name des <c>DialogKey</c>-Werts.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task RestoreAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            DialogSuppression? existing = await _context.DialogSuppressions
                .AsTracking()
                .FirstOrDefaultAsync(d => d.Key == key, cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                return;
            }

            existing.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("Dialog '{Key}' wird wieder angezeigt.", key);
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> RestoreAllAsync(CancellationToken cancellationToken = default)
        {
            List<DialogSuppression> active = await _context.DialogSuppressions
                .AsTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (active.Count == 0)
            {
                return 0;
            }

            DateTime now = EntityClock.Current.UtcNow;
            foreach (DialogSuppression suppression in active)
            {
                suppression.MarkAsDeleted(now);
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("{Count} ausgeblendete Dialoge werden wieder angezeigt.", active.Count);
            return active.Count;
        }
    }
}
