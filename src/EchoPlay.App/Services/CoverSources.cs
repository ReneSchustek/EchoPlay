namespace EchoPlay.App.Services
{
    /// <summary>
    /// Wo der Cover-Dienst Cover liest, speichert und herunterlädt, und die Drossel für die Abfragen
    /// beim Anbieter. Alle Phasen des Dienstes brauchen sie gemeinsam.
    /// </summary>
    public sealed record CoverSources
    {
        /// <summary>Liest und speichert Cover in der Datenbank.</summary>
        public required ICoverService CoverService { get; init; }

        /// <summary>Lädt Cover vom Anbieter herunter.</summary>
        public required ICoverDownloader CoverDownloader { get; init; }

        /// <summary>Drosselt die Abfragen beim Anbieter; ohne ihn ungedrosselt.</summary>
        public IHostRateLimiter? RateLimiter { get; init; }
    }
}
