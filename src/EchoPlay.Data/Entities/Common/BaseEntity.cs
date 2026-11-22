using EchoPlay.Core.Replication;
using EchoPlay.Data.Entities.Contracts;
using EchoPlay.Data.Internal;

namespace EchoPlay.Data.Entities.Common
{
    /// <summary>
    /// Basisklasse für alle persistierten Entitäten.
    /// Enthält ausschließlich technische Lebenszyklusinformationen wie Identität, Zeitstempel und Soft-Delete-Zustand.
    /// <para>
    /// Alle Properties haben <c>protected set</c>; sie können nur über die definierten Methoden
    /// verändert werden. Das verhindert, dass z.B. <c>IsDeleted = true</c> direkt von außen gesetzt
    /// wird und dabei Zeitstempel oder Idempotenzlogik umgangen werden.
    /// </para>
    /// </summary>
    public abstract class BaseEntity : ISoftDeletable
    {
        /// <summary>
        /// Eindeutige Identifikation der Entität.
        /// EF Core vergibt die Id, sobald die Entität dem Kontext hinzugefügt wird (<c>Add</c>);
        /// vorher ist sie <see cref="Guid.Empty"/>.
        /// </summary>
        [DataScope(DataScope.LocalIdentity)]
        public Guid Id { get; protected set; }

        /// <summary>
        /// Zeitpunkt der Erstellung (UTC).
        /// Wird bei der Instanziierung automatisch auf den aktuellen UTC-Zeitpunkt gesetzt.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public DateTime CreatedAt { get; protected set; } = EntityClock.Current.UtcNow;

        /// <summary>
        /// Zeitpunkt der letzten Änderung (UTC).
        /// </summary>
        [DataScope(DataScope.Shared)]
        public DateTime? UpdatedAt { get; protected set; }

        /// <summary>
        /// Gibt an, ob die Entität logisch gelöscht wurde.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public bool IsDeleted { get; protected set; }

        /// <summary>
        /// Zeitpunkt der logischen Löschung (UTC).
        /// </summary>
        [DataScope(DataScope.Shared)]
        public DateTime? DeletedAt { get; protected set; }

        /// <summary>
        /// Zeitpunkt der letzten Änderung eines gemeinsamen Felds auf der hybriden logischen Uhr,
        /// in der Textform von <see cref="HybridTimestamp"/>. Grundlage des Abgleichs zwischen
        /// Geräten: Bei zwei Fassungen derselben Zeile gewinnt die mit dem späteren Zeitpunkt.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> für Zeilen, die seit Einführung der Uhr nicht geändert wurden, und
        /// für Tabellen ohne gemeinsame Felder. Gesetzt wird der Wert beim Speichern
        /// (<c>ChangeStampInterceptor</c>) und von den Massenänderungen, die am Speichern vorbeigehen.
        /// </remarks>
        [DataScope(DataScope.Shared)]
        public string? ChangeStamp { get; protected set; }

        /// <summary>
        /// Markiert die Entität als logisch gelöscht. Der Vorgang ist idempotent.
        /// </summary>
        /// <param name="deletedAt">Zeitpunkt der Löschung (UTC).</param>
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
        /// Hebt eine logische Löschung auf. Der Abgleich braucht das, wenn ein anderes Gerät den
        /// Datensatz nach der Löschung wieder angelegt hat und damit jünger ist.
        /// </summary>
        public void MarkAsRestored()
        {
            IsDeleted = false;
            DeletedAt = null;
        }

        /// <summary>
        /// Markiert die Entität als geändert.
        /// </summary>
        /// <param name="updatedAt">Zeitpunkt der Änderung (UTC).</param>
        public void MarkAsUpdated(DateTime updatedAt)
        {
            UpdatedAt = updatedAt;
        }

        /// <summary>
        /// Setzt den Zeitpunkt der letzten gemeinsamen Änderung.
        /// </summary>
        /// <param name="stamp">Der Zeitpunkt der hybriden logischen Uhr.</param>
        public void MarkAsChanged(HybridTimestamp stamp)
        {
            ChangeStamp = stamp.ToString();
        }
    }
}
