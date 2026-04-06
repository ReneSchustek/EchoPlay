using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Konfigurierbarer Fake des iTunes-Search-Clients für Scoring-Tests.
    /// Erlaubt die gezielte Zuordnung von Alben zu Künstlern und Tracks zu Alben,
    /// um alle Stufen des Scorings deterministisch testen zu können.
    /// </summary>
    internal sealed class ConfigurableAppleMusicSearchClient : IAppleMusicSearchClient
    {
        private readonly Dictionary<long, List<ITunesCollectionDto>> _albumsByArtist = new();
        private readonly Dictionary<long, List<ITunesTrackDto>> _tracksByAlbum = new();

        /// <summary>
        /// Registriert Alben für einen bestimmten Künstler.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="albums">Die zugeordneten Alben.</param>
        /// <returns>Diese Instanz für Fluent-Konfiguration.</returns>
        public ConfigurableAppleMusicSearchClient WithAlbums(long artistId, List<ITunesCollectionDto> albums)
        {
            _albumsByArtist[artistId] = albums;
            return this;
        }

        /// <summary>
        /// Registriert Tracks für ein bestimmtes Album.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID.</param>
        /// <param name="tracks">Die zugeordneten Tracks.</param>
        /// <returns>Diese Instanz für Fluent-Konfiguration.</returns>
        public ConfigurableAppleMusicSearchClient WithTracks(long collectionId, List<ITunesTrackDto> tracks)
        {
            _tracksByAlbum[collectionId] = tracks;
            return this;
        }

        /// <summary>
        /// Wird von Scoring-Tests nicht benötigt, liefert leeres Ergebnis.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <returns>Leere Suchantwort.</returns>
        public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            return Task.FromResult(new ITunesResponseDto<ITunesArtistDto>());
        }

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            return Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>());
        }

        /// <summary>
        /// Liefert die konfigurierten Alben für den angegebenen Künstler.
        /// Das erste Element ist ein Artist-Eintrag (simuliert das Verhalten der iTunes Lookup API).
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <returns>Die Lookup-Antwort mit Artist- und Album-Einträgen.</returns>
        public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
        {
            List<ITunesCollectionDto> results = [];

            // Lookup-Antworten enthalten den Künstler als erstes Element
            results.Add(new ITunesCollectionDto { WrapperType = "artist" });

            if (_albumsByArtist.TryGetValue(artistId, out List<ITunesCollectionDto>? albums))
            {
                results.AddRange(albums);
            }

            ITunesResponseDto<ITunesCollectionDto> response = new()
            {
                ResultCount = results.Count,
                Results = results
            };

            return Task.FromResult(response);
        }

        /// <summary>
        /// Liefert die konfigurierten Tracks für das angegebene Album.
        /// Das erste Element ist ein Collection-Eintrag (simuliert das Verhalten der iTunes Lookup API).
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID.</param>
        /// <returns>Die Lookup-Antwort mit Collection- und Track-Einträgen.</returns>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default)
        {
            List<ITunesTrackDto> results = [];

            // Lookup-Antworten enthalten das Album als erstes Element
            results.Add(new ITunesTrackDto { WrapperType = "collection" });

            if (_tracksByAlbum.TryGetValue(collectionId, out List<ITunesTrackDto>? tracks))
            {
                results.AddRange(tracks);
            }

            ITunesResponseDto<ITunesTrackDto> response = new()
            {
                ResultCount = results.Count,
                Results = results
            };

            return Task.FromResult(response);
        }
    }
}
