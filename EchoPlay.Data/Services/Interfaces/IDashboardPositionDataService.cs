using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Schnittstelle für den Zugriff auf benutzerdefinierte Dashboard-Positionen.
    /// Ermöglicht das Speichern und Laden der Sortierreihenfolge von Serien
    /// in verschiedenen Dashboard-Bereichen (z.B. Neuerscheinungen, Favoriten).
    /// </summary>
    public interface IDashboardPositionDataService
    {
        /// <summary>
        /// Liefert alle Positionen für einen Dashboard-Bereich, aufsteigend nach Position sortiert.
        /// </summary>
        /// <param name="section">Der Dashboard-Bereich, z.B. "Neuerscheinungen" oder "Favoriten".</param>
        /// <returns>
        /// Positionen des Bereichs, sortiert nach <see cref="DashboardPosition.Position"/>.
        /// Leere Liste wenn keine benutzerdefinierte Sortierung für den Bereich existiert.
        /// </returns>
        Task<IReadOnlyList<DashboardPosition>> GetBySectionAsync(string section);

        /// <summary>
        /// Speichert die Sortierreihenfolge für einen Dashboard-Bereich.
        /// Bestehende Positionen des Bereichs werden vollständig ersetzt –
        /// damit ist die Reihenfolge nach jedem Speichern konsistent.
        /// </summary>
        /// <param name="section">Der Dashboard-Bereich.</param>
        /// <param name="seriesIds">
        /// Die Serien-IDs in der gewünschten Reihenfolge.
        /// Index 0 = Position 0 (ganz oben), Index 1 = Position 1, usw.
        /// </param>
        Task SaveOrderAsync(string section, IReadOnlyList<Guid> seriesIds);
    }
}
