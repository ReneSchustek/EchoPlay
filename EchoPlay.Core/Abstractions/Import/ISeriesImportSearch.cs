using EchoPlay.Core.Models.Import;

namespace EchoPlay.Core.Abstractions.Import
{
    /// <summary>
    /// Definiert den fachlichen Vertrag zur Suche nach importierbaren Hörspielserien aus externen Quellen.
    /// </summary>
    public interface ISeriesImportSearch
    {
        /// <summary>
        /// Sucht nach potenziellen Hörspielserien anhand eines freien Suchbegriffs.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine fachlich bewertete Liste importierbarer Serien.</returns>
        Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default);
    }
}
