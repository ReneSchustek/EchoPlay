using EchoPlay.Data.Entities.Common;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Repräsentiert eine einzelne lokale Audiodatei, die einer Episode zugeordnet ist.
    /// Wird beim Scan der lokalen Bibliothek erstellt.
    /// </summary>
    public sealed class LocalTrack : BaseEntity
    {
        /// <summary>
        /// Fremdschlüssel zur zugehörigen Episode.
        /// </summary>
        public Guid EpisodeId { get; set; }

        /// <summary>
        /// Zugehörige Episode.
        /// </summary>
        public Episode Episode { get; set; } = null!;

        /// <summary>
        /// Absoluter Dateipfad zur Audiodatei.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Position des Tracks innerhalb der Episode (1-basiert).
        /// </summary>
        public int TrackNumber { get; set; }

        /// <summary>
        /// Abspieldauer der Audiodatei.
        /// </summary>
        public TimeSpan Duration { get; set; }
    }
}
