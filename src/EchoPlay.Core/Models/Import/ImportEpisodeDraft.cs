namespace EchoPlay.Core.Models.Import
{
    /// <summary>
    /// Die Felder einer Folge, wie der Anbieter sie liefert. Nummer und Gesamtdauer leitet die Fabrik
    /// daraus ab.
    /// </summary>
    public sealed record ImportEpisodeDraft
    {
        /// <summary>Die Quell-ID der Episode (Album-/Collection-ID).</summary>
        public required string SourceEpisodeId { get; init; }

        /// <summary>Der Episodentitel.</summary>
        public required string Title { get; init; }

        /// <summary>Das Veröffentlichungsdatum, falls bekannt.</summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>Sortierposition innerhalb der Serie.</summary>
        public int OrderIndex { get; init; }

        /// <summary>Die geprüfte Anbieter-Adresse der Folge; <see langword="null"/>, wenn die Antwort keine brauchbare lieferte.</summary>
        public Uri? ProviderUrl { get; init; }

        /// <summary>Die geprüfte Cover-Adresse, falls vorhanden.</summary>
        public Uri? CoverImageUrl { get; init; }

        /// <summary>Der Quell-Bezeichner (z.B. "Spotify", "AppleMusic").</summary>
        public required string Source { get; init; }
    }
}
