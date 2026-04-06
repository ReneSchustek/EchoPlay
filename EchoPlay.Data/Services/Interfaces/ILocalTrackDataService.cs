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
        /// Ersetzt alle lokalen Tracks einer Episode durch die übergebene Liste.
        /// Bestehende Einträge werden entfernt, die neuen werden hinzugefügt.
        /// Diese Methode ist idempotent – ein erneuter Scan überschreibt den vorherigen.
        /// </summary>
        /// <param name="episodeId">ID der Episode.</param>
        /// <param name="tracks">Die neuen Tracks. Die <see cref="LocalTrack.EpisodeId"/> muss bereits gesetzt sein.</param>
        Task SaveTracksForEpisodeAsync(Guid episodeId, IReadOnlyList<LocalTrack> tracks);
    }
}
