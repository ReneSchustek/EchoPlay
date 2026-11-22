namespace EchoPlay.Data.Entities.Contracts
{
    /// <summary>
    /// Entität mit logischer Löschung: Ein gelöschter Datensatz ist ausgeblendet, bleibt aber stehen,
    /// bis die Datenbankpflege ihn nach der eingestellten Frist endgültig entfernt.
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
