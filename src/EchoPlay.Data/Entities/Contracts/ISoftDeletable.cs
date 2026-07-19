namespace EchoPlay.Data.Entities.Contracts
{
    /// <summary>
    /// Definiert den Vertrag für Entitäten, die eine logische Löschung unterstützen.
    /// Dies ermöglicht das Beibehalten von Datensätzen für Auditing-Zwecke, während sie für die UI unsichtbar werden.
    /// </summary>
    public interface ISoftDeletable
    {
        /// <summary>
        /// Gibt an, ob der Datensatz als gelöscht markiert wurde.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Der exakte UTC-Zeitstempel, an dem die Löschung vollzogen wurde.
        /// </summary>
        DateTime? DeletedAt { get; }

        /// <summary>
        /// Führt die logische Löschung der Entität durch.
        /// </summary>
        /// <param name="deletedAt">Der Zeitpunkt der Löschung.</param>
        void MarkAsDeleted(DateTime deletedAt);
    }
}
