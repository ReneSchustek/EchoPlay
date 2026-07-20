using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;

namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Speichert die benutzerdefinierte Sortierposition einer Serie in einem Dashboard-Bereich.
    /// Pro Serie und Bereich existiert maximal ein Eintrag – die Kombination aus
    /// <see cref="SeriesId"/> und <see cref="Section"/> ist eindeutig.
    /// Wird von der Drag-&amp;-Drop-Logik auf dem Dashboard geschrieben und beim Laden
    /// für die Sortierung der Kachelreihen ausgewertet.
    /// </summary>
    public class DashboardPosition : BaseEntity
    {
        /// <summary>
        /// Referenz auf die zugeordnete Serie (Fremdschlüssel).
        /// </summary>
        public Guid SeriesId { get; set; }

        /// <summary>
        /// Navigation-Property zur zugeordneten Serie.
        /// Wird von EF Core für die FK-Beziehung und Join-Abfragen verwendet.
        /// </summary>
        public Series Series { get; set; } = null!;

        /// <summary>
        /// Dashboard-Bereich, z.B. "Neuerscheinungen" oder "Favoriten".
        /// Zusammen mit <see cref="SeriesId"/> bildet dieser Wert den fachlichen Schlüssel.
        /// </summary>
        public string Section { get; set; } = string.Empty;

        /// <summary>
        /// Sortierposition innerhalb des Bereichs (0-basiert, aufsteigend).
        /// Kleinere Werte erscheinen weiter oben.
        /// </summary>
        public int Position { get; set; }
    }
}
