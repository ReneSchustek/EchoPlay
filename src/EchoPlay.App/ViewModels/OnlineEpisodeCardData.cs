namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Folgenkachel der Online-Mediathek anzeigt. Die Kachel bekommt dazu die Scope-Factory
    /// und die Uhr; so bleibt ihr Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record OnlineEpisodeCardData
    {
        /// <summary>Datenbank-ID der Episode.</summary>
        public required Guid EpisodeId { get; init; }

        /// <summary>Episodennummer oder null.</summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Episodentitel.</summary>
        public required string Title { get; init; }

        /// <summary>Erscheinungsdatum oder null.</summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>Ob die Episode als gehört markiert ist.</summary>
        public bool IsCompleted { get; init; }

        /// <summary>Geprüfte Adresse zum Öffnen beim Anbieter oder null.</summary>
        public Uri? ProviderUrl { get; init; }

        /// <summary>Apple-Music-Album-ID für Deep-Links. Null → Fallback auf Suchlink.</summary>
        public string? AppleMusicAlbumId { get; init; }

        /// <summary>Spotify-Album-ID für Deep-Links. Null → Fallback auf Suchlink.</summary>
        public string? SpotifyAlbumId { get; init; }
    }
}
