using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Gemerkter Serientitel, für den der Nutzer die Neuerscheinungs-Überwachung aktiviert hat.
    /// Überlebt das Leeren der Mediathek: <c>Series</c>-Zeilen werden dabei physisch
    /// gelöscht, die Überwachung wäre danach sonst für jede neu eingelesene Serie verloren.
    /// </summary>
    /// <remarks>
    /// Der Abgleich läuft über <see cref="NormalizedTitle"/>, weil derselbe Titel je nach Quelle
    /// unterschiedlich geschrieben ankommt (Ordnername vs. Provider-Antwort).
    /// </remarks>
    public sealed class WatchedTitle : BaseEntity
    {
        /// <summary>Serientitel in der Schreibweise, in der er gemerkt wurde (für Anzeige und Diagnose).</summary>
        [DataScope(DataScope.Shared)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Vergleichsform des Titels (<c>HoerspielTextNormalizer.Normalize</c>) als fachlicher Schlüssel.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string NormalizedTitle { get; set; } = string.Empty;
    }
}
