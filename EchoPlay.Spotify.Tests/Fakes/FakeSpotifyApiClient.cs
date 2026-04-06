using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.Fakes
{
    /// <summary>
    /// Fake-Implementierung des SpotifyApiClients für Tests.
    /// Der Fake ersetzt die Spotify-Web-API vollständig und liefert deterministische Daten aus dem Speicher.
    /// Dadurch werden Tests reproduzierbar und unabhängig von Netzwerk, Authentifizierung und externen Systemen.
    /// </summary>
    /// <remarks>Erstellt einen Fake-Client mit einer festen Künstlerliste.
    /// Für frühe Tests reicht diese Einschränkung aus, da Album- und Track-Daten noch nicht relevant sind.</remarks>
    /// <param name="artists">Die verfügbaren Test-Künstler.</param>
    internal sealed class FakeSpotifyApiClient(IReadOnlyList<SpotifyArtistDto> artists) : ISpotifyApiClient
    {
        private readonly IReadOnlyList<SpotifyArtistDto> _artists = artists;

        /// <summary>
        /// Simuliert die Spotify-Künstlersuche.
        /// Die Filterung erfolgt bewusst einfach, um das Verhalten leicht nachvollziehbar zu halten.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <returns>Gefundene Test-Künstler.</returns>
        public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit)
        {
            IReadOnlyList<SpotifyArtistDto> result = [.. _artists
                    .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(limit)];

            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit)
        {
            return Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
        }

        /// <summary>
        /// Liefert für Tests keine Alben zurück.
        /// Diese Methode ist vorhanden, um das Interface vollständig zu erfüllen, wird in den aktuellen Tests jedoch nicht genutzt.
        /// </summary>
        public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit)
        {
            return Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
        }

        /// <summary>
        /// Liefert für Tests keine Tracks zurück.
        /// Auch diese Methode dient aktuell nur der Vertragstreue.
        /// </summary>
        public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId)
        {
            return Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
        }
    }
}