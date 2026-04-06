using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Szenario-basierter Fake für den iTunes-Search-Client.
    /// Dieser Fake bildet gezielt eine typische Hörspielstruktur ab,
    /// um den Episodenimport fachlich vollständig testen zu können.
    /// </summary>
    internal sealed class ScenarioAppleMusicSearchClient : IAppleMusicSearchClient
    {
        /// <summary>
        /// Liefert ein leeres Suchergebnis, da dieser Fake nur für Episodenimport-Tests gedacht ist.
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
        /// Liefert zwei Alben in bewusst unsortierter Reihenfolge,
        /// um die korrekte Episodenaggregation zu testen.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <returns>Lookup-Antwort mit Artist- und Album-Einträgen.</returns>
        public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
        {
            ITunesResponseDto<ITunesCollectionDto> response = new()
            {
                ResultCount = 3,
                Results =
                [
                    // Erstes Element ist immer der Künstler
                    new ITunesCollectionDto { WrapperType = "artist" },
                    new ITunesCollectionDto
                    {
                        WrapperType = "collection",
                        CollectionId = 2002,
                        CollectionName = "Späteres Album",
                        ArtistId = artistId,
                        ArtistName = "Testkünstler",
                        TrackCount = 1,
                        ReleaseDate = "1985-01-01T07:00:00Z",
                        PrimaryGenreName = "Hörspiele"
                    },
                    new ITunesCollectionDto
                    {
                        WrapperType = "collection",
                        CollectionId = 1001,
                        CollectionName = "Früheres Album",
                        ArtistId = artistId,
                        ArtistName = "Testkünstler",
                        TrackCount = 1,
                        ReleaseDate = "1979-01-01T07:00:00Z",
                        PrimaryGenreName = "Hörspiele"
                    }
                ]
            };

            return Task.FromResult(response);
        }

        /// <summary>
        /// Liefert genau einen Track pro Album.
        /// Der Trackname enthält den Albumnamen, um die Zuordnung in Assertions zu erleichtern.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID.</param>
        /// <returns>Lookup-Antwort mit Collection- und Track-Einträgen.</returns>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default)
        {
            string albumName = collectionId == 1001 ? "Früheres Album" : "Späteres Album";

            ITunesResponseDto<ITunesTrackDto> response = new()
            {
                ResultCount = 2,
                Results =
                [
                    // Erstes Element ist immer das Album
                    new ITunesTrackDto { WrapperType = "collection" },
                    new ITunesTrackDto
                    {
                        WrapperType = "track",
                        TrackId = collectionId * 10,
                        TrackName = $"Episode aus {albumName}",
                        TrackTimeMillis = (int)TimeSpan.FromMinutes(45).TotalMilliseconds,
                        TrackNumber = 1,
                        ReleaseDate = collectionId == 1001 ? "1979-01-01T07:00:00Z" : "1985-01-01T07:00:00Z",
                        CollectionId = collectionId,
                        CollectionName = albumName
                    }
                ]
            };

            return Task.FromResult(response);
        }
    }
}
