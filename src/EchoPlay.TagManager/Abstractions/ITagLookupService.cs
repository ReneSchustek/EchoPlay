using EchoPlay.TagManager.Models;

namespace EchoPlay.TagManager.Abstractions
{
    /// <summary>
    /// Sucht Online-Metadaten für Audiodateien über eine externe Datenbank.
    /// Die konkrete Implementierung nutzt die MusicBrainz-REST-API,
    /// die kostenlos und ohne API-Key nutzbar ist (max. 1 Anfrage/Sekunde).
    /// </summary>
    public interface ITagLookupService
    {
        /// <summary>
        /// Sucht nach Releases, die zum Suchbegriff passen.
        /// Typische Suchbegriffe sind Albumtitel, Interpret oder eine Kombination aus beidem.
        /// </summary>
        /// <param name="query">Freitext-Suchanfrage, z.B. <c>"TKKG 200"</c> oder <c>"Die drei ???"</c>.</param>
        /// <param name="cancellationToken">Token zum Abbrechen der Anfrage.</param>
        /// <returns>
        /// Eine Liste von <see cref="TagLookupResult"/>-Objekten, sortiert nach Relevanz.
        /// Gibt eine leere Liste zurück, wenn kein Ergebnis gefunden wurde.
        /// </returns>
        /// <exception cref="System.Net.Http.HttpRequestException">
        /// Wird geworfen, wenn die MusicBrainz-API nicht erreichbar ist.
        /// </exception>
        Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
    }
}
