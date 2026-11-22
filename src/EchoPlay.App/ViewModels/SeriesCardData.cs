namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Serienkarte anzeigt. Die Karte bekommt dazu ihr Cover und ihre Dienste; so bleibt ihr
    /// Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record SeriesCardData
    {
        /// <summary>Datenbank-ID der Serie.</summary>
        public required Guid Id { get; init; }

        /// <summary>Titel der Serie.</summary>
        public required string Title { get; init; }

        /// <summary>Gesamtanzahl der Episoden.</summary>
        public int TotalEpisodeCount { get; init; }

        /// <summary>Anzahl noch nicht angehörter Episoden (NotStarted).</summary>
        public int NewEpisodeCount { get; init; }

        /// <summary>Anzahl begonnener Episoden (InProgress).</summary>
        public int InProgressCount { get; init; }

        /// <summary>Anzahl vollständig gehörter Episoden (Finished).</summary>
        public int FinishedCount { get; init; }

        /// <summary>Aktueller Abonnementstatus.</summary>
        public bool IsSubscribed { get; init; }

        /// <summary>Aktueller Favoritenstatus.</summary>
        public bool IsFavorite { get; init; }

        /// <summary>Aktueller Überwachungsstatus für Neuerscheinungen.</summary>
        public bool IsWatched { get; init; }
    }
}
