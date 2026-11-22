using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;

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
        [DataScope(DataScope.LocalIdentity)]
        public Guid EpisodeId { get; set; }

        /// <summary>
        /// Zugehörige Episode.
        /// </summary>
        public Episode Episode { get; set; } = null!;

        /// <summary>
        /// Absoluter Dateipfad zur Audiodatei.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Position des Tracks innerhalb der Episode (1-basiert).
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public int TrackNumber { get; set; }

        /// <summary>
        /// Abspieldauer der Audiodatei.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public TimeSpan Duration { get; set; }
    }
}
