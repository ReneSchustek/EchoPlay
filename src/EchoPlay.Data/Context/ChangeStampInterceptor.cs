using System.Collections.Concurrent;
using System.Reflection;
using EchoPlay.Core.Replication;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Setzt beim Speichern den Zeitpunkt der hybriden logischen Uhr an jeder Zeile, deren
    /// gemeinsame Felder sich geändert haben.
    /// </summary>
    /// <remarks>
    /// Die Uhr läuft nur bei gemeinsamen Feldern weiter. Liefe sie auch, wenn sich ein
    /// gerätegebundenes Feld ändert, etwa die Spurenzahl nach dem Einlesen, dann trüge diese Zeile
    /// im Abgleich den späteren Zeitpunkt und überschriebe mit ihren alten Favoriten eine echte
    /// Änderung auf dem anderen Gerät. <c>CreatedAt</c> und <c>UpdatedAt</c> zählen nicht als
    /// Änderung; sie wandern mit jeder Änderung mit und sagen nichts darüber, was sich geändert hat.
    /// <para>
    /// Massenänderungen über <c>ExecuteUpdateAsync</c> gehen am Speichern vorbei und setzen den
    /// Zeitpunkt selbst.
    /// </para>
    /// </remarks>
    /// <param name="clock">Die Uhr dieses Geräts.</param>
    public sealed class ChangeStampInterceptor(IHybridClock clock) : SaveChangesInterceptor
    {
        private static readonly ConcurrentDictionary<Type, bool> HasSharedFields = new();
        private static readonly ConcurrentDictionary<PropertyInfo, bool> IsSharedField = new();

        private readonly IHybridClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        /// <inheritdoc/>
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            Stamp(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            Stamp(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Stamp(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            foreach (EntityEntry<BaseEntity> entry in context.ChangeTracker.Entries<BaseEntity>())
            {
                if (NeedsStamp(entry))
                {
                    entry.Entity.MarkAsChanged(_clock.Now());
                }
            }
        }

        private static bool NeedsStamp(EntityEntry<BaseEntity> entry)
        {
            if (!HasSharedFields.GetOrAdd(entry.Entity.GetType(), DeclaresSharedFields))
            {
                return false;
            }

            // Ein ausdrücklich gesetzter Zeitpunkt bleibt: Der Abgleich übernimmt fremde Änderungen mit
            // dem Zeitpunkt ihres Geräts. Bekämen sie hier einen neuen, liefen sie als eigene
            // Änderung zurück und gewönnen gegen jede spätere des Absenders.
            if (entry.Property(e => e.ChangeStamp).IsModified
                || (entry.State == EntityState.Added && entry.Entity.ChangeStamp is not null))
            {
                return false;
            }

            return entry.State switch
            {
                EntityState.Added => true,
                EntityState.Modified => entry.Properties.Any(p => p.IsModified && CountsAsChange(p.Metadata.PropertyInfo)),
                _ => false,
            };
        }

        /// <summary>
        /// Ob der Typ gemeinsame Felder außerhalb der Basisklasse hat. Tabellen ohne solche, etwa die
        /// Cover oder die lokalen Spuren, tragen keinen Zeitpunkt; sie werden nicht abgeglichen.
        /// </summary>
        private static bool DeclaresSharedFields(Type type) => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(BaseEntity))
            .Any(p => p.GetCustomAttribute<DataScopeAttribute>()?.Scope == DataScope.Shared);

        private static bool CountsAsChange(PropertyInfo? property)
        {
            if (property is null
                || property.Name is nameof(BaseEntity.CreatedAt) or nameof(BaseEntity.UpdatedAt) or nameof(BaseEntity.ChangeStamp))
            {
                return false;
            }

            return IsSharedField.GetOrAdd(
                property,
                p => p.GetCustomAttribute<DataScopeAttribute>()?.Scope == DataScope.Shared);
        }
    }
}
