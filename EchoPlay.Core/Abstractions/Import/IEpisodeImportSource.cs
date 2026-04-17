using EchoPlay.Core.Models.Import;

namespace EchoPlay.Core.Abstractions.Import
{
    /// <summary>
    /// Definiert den Zugriff auf Episoden einer importierbaren Hörspielserie.
    /// </summary>
    public interface IEpisodeImportSource
    {
        /// <summary>
        /// Lädt alle Episoden zu einer importierbaren Serie.
        /// </summary>
        /// <param name="sourceSeriesId">Die Serienkennung innerhalb der Importquelle.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine sortierte Liste importierbarer Episoden.</returns>
        Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(string sourceSeriesId, CancellationToken cancellationToken = default);
    }
}
