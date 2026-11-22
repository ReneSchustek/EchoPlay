using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Speichert Cover-Binärdaten getrennt von Metadaten.
    /// Eine Zeile pro Entity (Serie oder Episode), verknüpft über EntityType + EntityId.
    /// Die Trennung verhindert, dass Metadaten-Queries MB an Bilddaten mitladen.
    /// </summary>
    public sealed class CoverImage : BaseEntity
    {
        /// <summary>
        /// Typ der verknüpften Entity: "Series" oder "Episode".
        /// </summary>
        [DataScope(DataScope.Cache)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// ID der verknüpften Serie oder Episode.
        /// </summary>
        [DataScope(DataScope.LocalIdentity)]
        public Guid EntityId { get; set; }

        /// <summary>
        /// Cover-Binärdaten (JPEG/PNG).
        /// </summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
            Justification = "EF Core speichert BLOBs als byte[]; Collection<byte> würde die EF-Mapping-Konvention brechen.")]
        [DataScope(DataScope.Cache)]
        public byte[] ImageData { get; set; } = [];

        /// <summary>
        /// SHA-256-Hash der <see cref="ImageData"/> als Hex-String (64 Zeichen).
        /// Dient zur Erkennung von Blob-Korruption durch Storage-Probleme.
        /// </summary>
        [DataScope(DataScope.Cache)]
        public string? SourceHash { get; set; }

        /// <summary>
        /// URL der ursprünglichen Cover-Quelle (Spotify, Apple Music, iTunes).
        /// Dient als Fallback für erneuten Download bei Datenverlust.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "TEXT-Spalte mit gewachsenem Bestand. Als Uri ließe eine einzige unlesbare Zeile beim Materialisieren die ganze Abfrage scheitern; wer die Adresse braucht, liest sie über WebAddress.")]
        [DataScope(DataScope.Cache)]
        public string? SourceUrl { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten automatischen Cover-Suche (UTC).
        /// Null = noch nie gesucht. Wird auch bei Nicht-Treffer gesetzt (Cooldown).
        /// </summary>
        [DataScope(DataScope.Cache)]
        public DateTime? LastChecked { get; set; }
    }
}
