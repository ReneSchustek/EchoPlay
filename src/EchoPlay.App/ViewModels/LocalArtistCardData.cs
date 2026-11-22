namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Serienkachel der lokalen Mediathek anzeigt. Die Kachel bekommt dazu ihr Cover und die
    /// Scope-Factory; so bleibt ihr Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record LocalArtistCardData
    {
        /// <summary>Datenbank-ID der Serie.</summary>
        public required Guid SeriesId { get; init; }

        /// <summary>Titel der Serie.</summary>
        public required string Title { get; init; }

        /// <summary>Lokaler Serienordner für den "Alle Tracks"-Button.</summary>
        public string? LocalFolderPath { get; init; }

        /// <summary>Episoden mit mindestens einem lokal gefundenen Track.</summary>
        public int LocalEpisodeCount { get; init; }

        /// <summary>Gesamtanzahl aller Episoden dieser Serie.</summary>
        public int TotalEpisodeCount { get; init; }

        /// <summary>Ob die Serie aktuell als Favorit markiert ist.</summary>
        public bool IsFavorite { get; init; }

        /// <summary>Ob die Serie auf Neuerscheinungen überwacht wird.</summary>
        public bool IsWatched { get; init; }

        /// <summary>Ob der Serie Folgen fehlen, berechnet von <see cref="Services.IncompleteSeries"/>.</summary>
        public bool IsIncomplete { get; init; }
    }
}
