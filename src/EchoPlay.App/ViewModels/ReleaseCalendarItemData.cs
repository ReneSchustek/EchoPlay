namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was ein Eintrag des Veröffentlichungskalenders anzeigt; so bleibt der Konstruktor kurz und jeder
    /// Wert benannt.
    /// </summary>
    public sealed record ReleaseCalendarItemData
    {
        /// <summary>Kennung der Serie; ein Klick öffnet ihre Detailseite.</summary>
        public required Guid SeriesId { get; init; }

        /// <summary>Name der Serie.</summary>
        public required string SeriesName { get; init; }

        /// <summary>Albumname beim Anbieter.</summary>
        public required string EpisodeTitle { get; init; }

        /// <summary>Erscheinungstag und Folgennummer, fertig formatiert.</summary>
        public required string DateLine { get; init; }

        /// <summary>Adresse des Covers, oder <c>null</c>.</summary>
        public Uri? CoverUri { get; init; }

        /// <summary>Kennzeichen oben links, oder <c>null</c> für keines.</summary>
        public string? BadgeText { get; init; }

        /// <summary>Erscheinungszeitpunkt (UTC).</summary>
        public DateTime ReleaseDateUtc { get; init; }

        /// <summary>Ob die Folge noch nicht erschienen ist.</summary>
        public bool IsAnnounced { get; init; }

        /// <summary>Wo die Folge im Bestand liegt.</summary>
        public ReleaseAvailability Availability { get; init; }
    }
}
