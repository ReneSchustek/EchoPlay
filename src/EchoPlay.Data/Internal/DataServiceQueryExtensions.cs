using EchoPlay.Data.Entities.Common;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Internal
{
    /// <summary>
    /// Gemeinsame Query-Bausteine für die EF-Core-Datenservices.
    /// </summary>
    internal static class DataServiceQueryExtensions
    {
        /// <summary>
        /// Lädt eine Entität per Id im Tracking-Modus. Wird sie nicht gefunden, wird eine
        /// einheitliche Warnung protokolliert und <c>null</c> zurückgegeben – der Aufrufer
        /// bricht die Operation dann typischerweise ab.
        /// </summary>
        /// <typeparam name="TEntity">Der Entitätstyp (mit Id).</typeparam>
        /// <param name="set">Das abzufragende DbSet.</param>
        /// <param name="logger">Der Logger für die Nicht-gefunden-Warnung.</param>
        /// <param name="id">Die gesuchte Id.</param>
        /// <param name="entityLabel">Fachliche Bezeichnung der Entität für die Warnung (z.B. "Serie").</param>
        /// <param name="operation">Die übersprungene Operation für die Warnung (z.B. "Soft-Delete").</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die getrackte Entität oder <c>null</c>, wenn sie nicht existiert.</returns>
        public static async Task<TEntity?> LoadTrackedByIdOrWarnAsync<TEntity>(
            this DbSet<TEntity> set,
            ILogger logger,
            Guid id,
            string entityLabel,
            string operation,
            CancellationToken cancellationToken = default)
            where TEntity : BaseEntity
        {
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(logger);

            TEntity? entity = await set
                .AsTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);

            if (entity is null)
            {
                logger.Warning("{EntityLabel} mit ID '{Id}' nicht gefunden – {Operation} übersprungen.", entityLabel, id, operation);
            }

            return entity;
        }
    }
}
