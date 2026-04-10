using EchoPlay.Data.Entities.Library;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Definiert den Vertrag für den Zugriff auf lokale Audiodateien einer Episode.
    /// </summary>
    public interface ILocalTrackDataService
    {
        /// <summary>
        /// Lädt alle lokalen Tracks einer Episode, sortiert nach Tracknummer.
        /// Gibt eine leere Liste zurück, wenn noch kein Scan für die Episode durchgeführt wurde.
        /// </summary>
        /// <param name="episodeId">ID der Episode.</param>
        /// <returns>Alle bekannten lokalen Tracks der Episode.</returns>
        Task<IReadOnlyList<LocalTrack>> GetByEpisodeIdAsync(Guid episodeId);

        /// <summary>
        /// Lädt für mehrere Episoden gleichzeitig den jeweils ersten Track (nach Tracknummer)
        /// in einem einzigen Datenbank-Roundtrip. Vermeidet N+1-Queries beim Cover-Suchen
        /// für ganze Bibliotheken.
        /// </summary>
        /// <param name="episodeIds">IDs der Episoden, deren erster Track gesucht wird.</param>
        /// <returns>
        /// Dictionary von Episoden-ID zum ersten Track. Episoden ohne Tracks fehlen im Dictionary.
        /// </returns>
        Task<IReadOnlyDictionary<Guid, LocalTrack>> GetFirstTracksByEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds);

        /// <summary>
        /// Ersetzt alle lokalen Tracks einer Episode durch die übergebene Liste.
        /// Bestehende Einträge werden entfernt, die neuen werden hinzugefügt.
        /// Diese Methode ist idempotent – ein erneuter Scan überschreibt den vorherigen.
        /// </summary>
        /// <param name="episodeId">ID der Episode.</param>
        /// <param name="tracks">Die neuen Tracks. Die <see cref="LocalTrack.EpisodeId"/> muss bereits gesetzt sein.</param>
        Task SaveTracksForEpisodeAsync(Guid episodeId, IReadOnlyList<LocalTrack> tracks);
    }
}
