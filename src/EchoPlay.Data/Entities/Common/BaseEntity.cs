using EchoPlay.Data.Entities.Contracts;
using EchoPlay.Data.Internal;

namespace EchoPlay.Data.Entities.Common
{
    /// <summary>
    /// Basisklasse für alle persistierten Entitäten.
    /// Enthält ausschließlich technische Lebenszyklusinformationen wie Identität, Zeitstempel und Soft-Delete-Zustand.
    ///
    /// Alle Properties haben <c>protected set</c> – sie können nur über die definierten Methoden
    /// verändert werden. Das verhindert, dass z.B. <c>IsDeleted = true</c> direkt von außen gesetzt
    /// wird und dabei Zeitstempel oder Idempotenzlogik umgangen werden.
    /// </summary>
    public abstract class BaseEntity : ISoftDeletable
    {
        /// <summary>
        /// Eindeutige Identifikation der Entität.
        /// EF Core setzt die Id beim ersten <c>SaveChangesAsync</c> – vorher ist sie <see cref="Guid.Empty"/>.
        /// </summary>
        public Guid Id { get; protected set; }

        /// <summary>
        /// Zeitpunkt der Erstellung (UTC).
        /// Wird bei der Instanziierung automatisch auf den aktuellen UTC-Zeitpunkt gesetzt.
        /// </summary>
        public DateTime CreatedAt { get; protected set; } = EntityClock.Current.UtcNow;

        /// <summary>
        /// Zeitpunkt der letzten Änderung (UTC).
        /// </summary>
        public DateTime? UpdatedAt { get; protected set; }

        /// <summary>
        /// Gibt an, ob die Entität logisch gelöscht wurde.
        /// </summary>
        public bool IsDeleted { get; protected set; }

        /// <summary>
        /// Zeitpunkt der logischen Löschung (UTC).
        /// </summary>
        public DateTime? DeletedAt { get; protected set; }

        /// <summary>
        /// Markiert die Entität als logisch gelöscht.
        ///
        /// Der Vorgang ist idempotent.
        /// </summary>
        public void MarkAsDeleted(DateTime deletedAt)
        {
            if (IsDeleted)
            {
                return;
            }

            IsDeleted = true;
            DeletedAt = deletedAt;
        }

        /// <summary>
        /// Markiert die Entität als geändert.
        /// </summary>
        public void MarkAsUpdated(DateTime updatedAt)
        {
            UpdatedAt = updatedAt;
        }
    }
}
