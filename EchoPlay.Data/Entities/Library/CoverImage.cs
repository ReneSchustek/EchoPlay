using EchoPlay.Data.Entities.Common;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Speichert Cover-Binärdaten getrennt von Metadaten.
    /// Eine Zeile pro Entity (Serie oder Episode) – verknüpft über EntityType + EntityId.
    /// Die Trennung verhindert, dass Metadaten-Queries MB an Bilddaten mitladen.
    /// </summary>
    public class CoverImage : BaseEntity
    {
        /// <summary>
        /// Typ der verknüpften Entity: "Series" oder "Episode".
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// ID der verknüpften Serie oder Episode.
        /// </summary>
        public Guid EntityId { get; set; }

        /// <summary>
        /// Cover-Binärdaten (JPEG/PNG).
        /// </summary>
        public byte[] ImageData { get; set; } = [];

        /// <summary>
        /// URL der ursprünglichen Cover-Quelle (Spotify, Apple Music, iTunes).
        /// Dient als Fallback für erneuten Download bei Datenverlust.
        /// </summary>
        public string? SourceUrl { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten automatischen Cover-Suche (UTC).
        /// Null = noch nie gesucht. Wird auch bei Nicht-Treffer gesetzt (Cooldown).
        /// </summary>
        public DateTime? LastChecked { get; set; }
    }
}
