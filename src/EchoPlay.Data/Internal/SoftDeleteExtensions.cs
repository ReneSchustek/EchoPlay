using EchoPlay.Data.Entities.Contracts;

namespace EchoPlay.Data.Internal
{
    /// <summary>
    /// Erweiterungen für die Soft-Delete-Kaskade auf Kind-Entitäten.
    /// </summary>
    internal static class SoftDeleteExtensions
    {
        /// <summary>
        /// Markiert alle Entitäten der Sequenz als logisch gelöscht (zum aktuellen
        /// <see cref="EntityClock"/>-Zeitpunkt). Kapselt die wiederkehrende Kaskaden-Schleife.
        /// </summary>
        /// <param name="entities">Die logisch zu löschenden Entitäten.</param>
        public static void MarkRangeDeleted(this IEnumerable<ISoftDeletable> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);

            foreach (ISoftDeletable entity in entities)
            {
                entity.MarkAsDeleted(EntityClock.Current.UtcNow);
            }
        }
    }
}
