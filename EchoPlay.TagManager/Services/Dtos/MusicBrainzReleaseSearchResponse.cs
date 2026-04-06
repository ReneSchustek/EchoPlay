using System.Text.Json.Serialization;

namespace EchoPlay.TagManager.Services.Dtos
{
    /// <summary>
    /// Rohantwort der MusicBrainz-API auf eine Release-Suche.
    /// Entspricht dem JSON-Objekt unter dem Endpunkt <c>/ws/2/release?fmt=json</c>.
    /// </summary>
    internal sealed class MusicBrainzReleaseSearchResponse
    {
        /// <summary>Liste der gefundenen Releases, sortiert nach Relevanzscore.</summary>
        [JsonPropertyName("releases")]
        public List<MusicBrainzRelease> Releases { get; init; } = [];
    }

    /// <summary>
    /// Ein einzelnes Release aus der MusicBrainz-Suchantwort.
    /// </summary>
    internal sealed class MusicBrainzRelease
    {
        /// <summary>Titel des Releases.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// Erscheinungsdatum als Zeichenkette, z.B. <c>"2023"</c> oder <c>"2023-05-01"</c>.
        /// MusicBrainz liefert je nach Datenlage unterschiedliche Genauigkeiten.
        /// </summary>
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        /// <summary>Gesamtanzahl der Tracks auf diesem Release.</summary>
        [JsonPropertyName("track-count")]
        public uint TrackCount { get; init; }

        /// <summary>Liste der beteiligten Künstler mit optionalem Verbindungstext (z.B. "&amp; ").</summary>
        [JsonPropertyName("artist-credit")]
        public List<MusicBrainzArtistCredit> ArtistCredits { get; init; } = [];
    }

    /// <summary>
    /// Ein Künstler-Credit aus einem MusicBrainz-Release.
    /// </summary>
    internal sealed class MusicBrainzArtistCredit
    {
        /// <summary>Künstler-Objekt mit Name und ID.</summary>
        [JsonPropertyName("artist")]
        public MusicBrainzArtist? Artist { get; init; }
    }

    /// <summary>
    /// Ein Künstler aus MusicBrainz.
    /// </summary>
    internal sealed class MusicBrainzArtist
    {
        /// <summary>Kanonischer Name des Künstlers in MusicBrainz.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
