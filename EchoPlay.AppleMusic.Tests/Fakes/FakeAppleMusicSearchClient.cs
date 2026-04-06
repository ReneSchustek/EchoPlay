using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Fake-Implementierung des iTunes-Search-Clients für Tests.
    /// Der Fake ersetzt die iTunes Search API vollständig und liefert deterministische Daten aus dem Speicher.
    /// Dadurch werden Tests reproduzierbar und unabhängig von Netzwerk und externen Systemen.
    /// </summary>
    internal sealed class FakeAppleMusicSearchClient : IAppleMusicSearchClient
    {
        private readonly IReadOnlyList<ITunesArtistDto> _artists;
        private readonly Dictionary<long, List<ITunesCollectionDto>> _albumsByArtist = [];

        /// <summary>
        /// Initialisiert den Fake mit einer Liste von Künstlern.
        /// Alben werden separat über <see cref="AddAlbums"/> konfiguriert.
        /// </summary>
        /// <param name="artists">Die verfügbaren Test-Künstler.</param>
        public FakeAppleMusicSearchClient(IReadOnlyList<ITunesArtistDto> artists)
        {
            _artists = artists;
        }

        /// <summary>
        /// Erstellt einen Fake ohne vorkonfigurierte Künstler.
        /// </summary>
        public FakeAppleMusicSearchClient() : this([])
        {
        }

        /// <summary>
        /// Fügt Test-Alben für einen bestimmten Künstler hinzu.
        /// </summary>
        /// <param name="artistId">Die Künstler-ID, für die die Alben zurückgegeben werden sollen.</param>
        /// <param name="albums">Die Test-Alben.</param>
        public void AddAlbums(long artistId, List<ITunesCollectionDto> albums)
        {
            _albumsByArtist[artistId] = albums;
        }

        /// <summary>
        /// Simuliert die iTunes-Künstlersuche.
        /// Die Filterung erfolgt bewusst einfach, um das Verhalten leicht nachvollziehbar zu halten.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <returns>Gefundene Test-Künstler.</returns>
        public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            List<ITunesArtistDto> matched = _artists
                .Where(a => a.ArtistName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            ITunesResponseDto<ITunesArtistDto> response = new()
            {
                ResultCount = matched.Count,
                Results = matched
            };

            return Task.FromResult(response);
        }

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            return Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>());
        }

        /// <summary>
        /// Liefert die über <see cref="AddAlbums"/> konfigurierten Alben für den Künstler.
        /// Gibt eine leere Liste zurück wenn keine Alben konfiguriert wurden.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <returns>Lookup-Antwort mit den konfigurierten Alben.</returns>
        public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
        {
            List<ITunesCollectionDto> albums = _albumsByArtist.TryGetValue(artistId, out List<ITunesCollectionDto>? found)
                ? found
                : [];

            ITunesResponseDto<ITunesCollectionDto> response = new()
            {
                ResultCount = albums.Count,
                Results = albums
            };

            return Task.FromResult(response);
        }

        /// <summary>
        /// Liefert für Tests keine Tracks zurück.
        /// Diese Methode dient nur der Vertragstreue.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID.</param>
        /// <returns>Leere Lookup-Antwort.</returns>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default)
        {
            return Task.FromResult(new ITunesResponseDto<ITunesTrackDto>());
        }
    }
}
