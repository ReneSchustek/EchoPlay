namespace EchoPlay.Data.Entities.Contracts
{
    /// <summary>
    /// Ein Album des Anbieters, wie es Neuerscheinungen und Veröffentlichungskalender zwischenspeichern.
    /// </summary>
    /// <remarks>
    /// Beide Tabellen halten dieselben Felder unter demselben fachlichen Schlüssel
    /// (<see cref="CollectionId"/>), folgen aber verschiedenen Aufräumregeln und bleiben deshalb
    /// getrennt. Gemeinsam ist ihnen das Schreiben neuer Antworten des Anbieters.
    /// </remarks>
    public interface IProviderRelease
    {
        /// <summary>Kennung der Serie.</summary>
        Guid SeriesId { get; set; }

        /// <summary>Albumname beim Anbieter.</summary>
        string Title { get; set; }

        /// <summary>Aus dem Albumnamen gelesene Folgennummer.</summary>
        int? EpisodeNumber { get; set; }

        /// <summary>Erscheinungszeitpunkt (UTC).</summary>
        DateTime ReleaseDate { get; set; }

        /// <summary>
        /// Adresse des Album-Bilds. Der Name weicht von der Spalte <c>CoverUrl</c> ab, damit die
        /// Schnittstelle keine Text-Adresse unter dem Namen einer Uri anbietet.
        /// </summary>
        string? CoverAddress { get; set; }

        /// <summary>Sammlungs-Kennung des Anbieters; fachlicher Schlüssel.</summary>
        long CollectionId { get; set; }

        /// <summary>Zeitpunkt der Prüfung (UTC).</summary>
        DateTime CheckedAtUtc { get; set; }
    }
}
