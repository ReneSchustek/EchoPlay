namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Folgenkachel der lokalen Mediathek anzeigt. Die Kachel bekommt dazu ihr Cover; so
    /// bleibt ihr Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record LocalEpisodeCardData
    {
        /// <summary>Datenbank-ID der Episode.</summary>
        public required Guid EpisodeId { get; init; }

        /// <summary>Episodennummer oder null wenn keine Nummer vorhanden.</summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Episodentitel.</summary>
        public required string Title { get; init; }

        /// <summary>Anzahl der lokal zugeordneten Tracks.</summary>
        public int LocalTrackCount { get; init; }

        /// <summary>Ordnerpfad der Episode für den "Alle Tracks bearbeiten"-Button.</summary>
        public string? FolderPath { get; init; }

        /// <summary>Ob die Episode als gehört markiert ist.</summary>
        public bool IsCompleted { get; init; }

        /// <summary>Ob es sich um eine Sonderfolge handelt (Nummer 0, 000-Präfix oder ohne Nummer).</summary>
        public bool IsSpecialEpisode { get; init; }
    }
}
