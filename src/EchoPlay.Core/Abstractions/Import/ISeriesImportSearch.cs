using EchoPlay.Core.Models.Import;
using System.Runtime.CompilerServices;

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

        /// <summary>
        /// Sucht wie <see cref="SearchAsync"/>, meldet jeden Treffer aber, sobald er feststeht.
        /// Die Reihenfolge ist die Reihenfolge der Bewertung — der sicherste Treffer zuerst.
        /// </summary>
        /// <remarks>
        /// Die Standardfassung sammelt das Ergebnis von <see cref="SearchAsync"/> ein und gibt es
        /// am Stück aus. Anbieter, deren Bewertung je Kandidat einzeln fertig wird, überschreiben
        /// sie — dann steht der erste Treffer auf dem Schirm, während die übrigen noch laufen.
        /// </remarks>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Die Treffer in der Reihenfolge, in der sie feststehen.</returns>
        async IAsyncEnumerable<ImportSeries> SearchStreamAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ImportSeries> results = await SearchAsync(query, cancellationToken).ConfigureAwait(false);

            foreach (ImportSeries series in results)
            {
                yield return series;
            }
        }
    }
}
