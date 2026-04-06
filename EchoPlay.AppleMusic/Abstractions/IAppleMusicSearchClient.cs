using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Abstractions
{
    /// <summary>
    /// Definiert den technischen Zugriff auf die iTunes Search API.
    /// Das Interface kapselt den HTTP-Zugriff und trennt fachliche
    /// Importlogik von Transport- und Serialisierungsdetails.
    /// Die iTunes Search API ist kostenfrei und erfordert keine Authentifizierung.
    /// </summary>
    public interface IAppleMusicSearchClient
    {
        /// <summary>
        /// Sucht nach Künstlern anhand eines freien Suchbegriffs.
        /// Nutzt intern den Endpunkt /search mit entity=musicArtist.
        /// </summary>
        /// <param name="query">Der Suchbegriff.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse (Standard: 25).</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Suchantwort mit Künstler-Ergebnissen.</returns>
        Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(string query, int limit = 25, CancellationToken ct = default);

        /// <summary>
        /// Sucht Alben (Folgen) anhand eines Suchbegriffs.
        /// Nutzt die iTunes Search API mit entity=album.
        /// </summary>
        /// <param name="query">Der Suchtext (z.B. "Kapatenhund").</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Suchantwort mit Album-Ergebnissen.</returns>
        Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(string query, int limit = 25, CancellationToken ct = default);

        /// <summary>
        /// Lädt alle Alben eines Künstlers über die Lookup-API.
        /// Das erste Element der Antwort ist der Künstler selbst und muss gefiltert werden.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit gemischten Ergebnissen (Künstler + Alben).</returns>
        Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default);

        /// <summary>
        /// Lädt alle Tracks eines Albums über die Lookup-API.
        /// Das erste Element der Antwort ist das Album selbst und muss gefiltert werden.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID des Albums.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit gemischten Ergebnissen (Album + Tracks).</returns>
        Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default);
    }
}
