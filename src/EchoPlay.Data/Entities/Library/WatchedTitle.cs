using EchoPlay.Data.Entities.Common;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Gemerkter Serientitel, für den der Nutzer die Neuerscheinungs-Überwachung aktiviert hat.
    /// Überlebt bewusst das Leeren der Mediathek: <c>Series</c>-Zeilen werden dabei physisch
    /// gelöscht, die Überwachung wäre danach sonst für jede neu eingelesene Serie verloren.
    /// </summary>
    /// <remarks>
    /// Der Abgleich läuft über <see cref="NormalizedTitle"/>, weil derselbe Titel je nach Quelle
    /// unterschiedlich geschrieben ankommt (Ordnername vs. Provider-Antwort).
    /// </remarks>
    public class WatchedTitle : BaseEntity
    {
        /// <summary>Serientitel in der Schreibweise, in der er gemerkt wurde (für Anzeige und Diagnose).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Vergleichsform des Titels (<c>HoerspielTextNormalizer.Normalize</c>) – fachlicher Schlüssel.
        /// </summary>
        public string NormalizedTitle { get; set; } = string.Empty;
    }
}
