using EchoPlay.Core.Models.Import;

namespace EchoPlay.Core.Abstractions.Import
{
    /// <summary>
    /// Definiert den Zugriff auf Episoden einer importierbaren Hörspielserie.
    /// </summary>
    public interface IEpisodeImportSource
    {
        /// <summary>
        /// Lädt Episoden zu einer importierbaren Serie, neueste zuerst.
        /// </summary>
        /// <param name="sourceSeriesId">Die Serienkennung innerhalb der Importquelle.</param>
        /// <param name="knownEpisodeTitles">
        /// Optional: bereits bekannte Episoden-Titel. Für Alben mit diesem Titel entfällt der
        /// Track-Lookup (nur für die Dauer nötig); ihre Metadaten inkl. Cover-URL werden dennoch
        /// zurückgegeben, damit der Delta-Import ein fehlendes Cover nachtragen kann. So kostet ein
        /// Delta-Abgleich nur so viele Track-Lookups wie es neue Folgen gibt, statt einen pro
        /// bestehender Folge. <see langword="null"/> oder leer lädt alle Episoden vollständig (Voll-Import).
        /// </param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine nach Erscheinungsdatum absteigend sortierte Liste importierbarer Episoden.</returns>
        Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
            string sourceSeriesId,
            IReadOnlySet<string>? knownEpisodeTitles = null,
            CancellationToken cancellationToken = default);
    }
}
