using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Core.Models.Import
{
    /// <summary>
    /// Repräsentiert eine einzelne Episode einer importierbaren Hörspielserie.
    /// Die Episode ist fachlich aufbereitet und sortiert, sodass nachgelagerte Schichten keine Kenntnis
    /// über Anbieter- oder Trackstrukturen benötigen.
    /// </summary>
    public sealed class ImportEpisode
    {
        /// <summary>
        /// Eindeutige Kennung der Episode innerhalb der Importquelle.
        /// </summary>
        public required string SourceEpisodeId { get; init; }

        /// <summary>
        /// Titel der Episode.
        /// </summary>
        public required string Title { get; init; }

        /// <summary>
        /// Optionale Episodennummer innerhalb der Serie.
        /// </summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>
        /// Veröffentlichungsdatum der Episode, sofern vom Anbieter bereitgestellt.
        /// </summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>
        /// Gesamtdauer der Episode.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Fachlich bestimmte Sortierreihenfolge innerhalb der Serie.
        /// </summary>
        public int OrderIndex { get; init; }

        /// <summary>
        /// URL zum Öffnen der Folge beim Provider (Spotify/Apple Music).
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO spiegelt externes API-Format (iTunes/Spotify JSON); Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
        public string? ProviderUrl { get; init; }

        /// <summary>
        /// URL zum Album-Cover beim Provider.
        /// Wird beim Import heruntergeladen und als <c>LocalCoverData</c> in der Episode gespeichert.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO spiegelt externes API-Format (iTunes/Spotify JSON); Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
        public string? CoverImageUrl { get; init; }

        /// <summary>
        /// Bezeichner der Importquelle ("Spotify" oder "AppleMusic").
        /// Wird vom Mapper gesetzt, damit der ImportService die richtige Provider-ID-Spalte befüllen kann.
        /// </summary>
        public string? Source { get; init; }
    }
}
