namespace EchoPlay.App.Models
{
    /// <summary>
    /// Sortierkriterium für die Episodenliste in der <see cref="EchoPlay.App.Views.SeriesDetailPage"/>.
    /// </summary>
    public enum EpisodeSortOrder
    {
        /// <summary>Aufsteigend nach Episodennummer. Episoden ohne Nummer stehen am Ende.</summary>
        EpisodeNumber,

        /// <summary>Aufsteigend nach Episodentitel (alphabetisch).</summary>
        Title,

        /// <summary>
        /// Aufsteigend nach Erscheinungsdatum (<see cref="EchoPlay.Data.Entities.Library.Episode.ReleaseDate"/>).
        /// Episoden ohne Datum stehen am Ende.
        /// </summary>
        ReleaseDate
    }
}
