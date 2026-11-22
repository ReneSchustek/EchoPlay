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

        /// <summary>Ein Hörspiel-Album des Testkünstlers mit einem Titel.</summary>
        private static ITunesCollectionDto Album(long collectionId, string name, long artistId, string releaseDate)
            => new()
            {
                WrapperType = "collection",
                CollectionId = collectionId,
                CollectionName = name,
                ArtistId = artistId,
                ArtistName = "Testkünstler",
                TrackCount = 1,
                ReleaseDate = releaseDate,
                PrimaryGenreName = "Hörspiele"
            };

        /// <summary>
        /// Liefert zwei Alben in unsortierter Reihenfolge,
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
                    Album(2002, "Späteres Album", artistId, "1985-01-01T07:00:00Z"),
                    Album(1001, "Früheres Album", artistId, "1979-01-01T07:00:00Z")
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
            ITunesResponseDto<ITunesTrackDto> response = new()
            {
                ResultCount = 2,
                Results = [.. BuildAlbumWithTrack(collectionId)]
            };

            return Task.FromResult(response);
        }

        /// <summary>
        /// Batch-Variante: liefert je angefragtem Album dessen Collection-Eintrag plus Track,
        /// jeweils mit gesetzter CollectionId zur Zuordnung.
        /// </summary>
        /// <param name="collectionIds">Die iTunes-Collection-IDs des Batches.</param>
        /// <returns>Die Lookup-Antwort mit Collection- und Track-Einträgen aller Alben.</returns>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(IReadOnlyList<long> collectionIds, CancellationToken ct = default)
        {
            List<ITunesTrackDto> results = [];
            foreach (long collectionId in collectionIds)
            {
                results.AddRange(BuildAlbumWithTrack(collectionId));
            }

            ITunesResponseDto<ITunesTrackDto> response = new()
            {
                ResultCount = results.Count,
                Results = results
            };

            return Task.FromResult(response);
        }

        private static IReadOnlyList<ITunesTrackDto> BuildAlbumWithTrack(long collectionId)
        {
            string albumName = collectionId == 1001 ? "Früheres Album" : "Späteres Album";

            return
            [
                // Erstes Element ist immer das Album
                new ITunesTrackDto { WrapperType = "collection", CollectionId = collectionId },
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
            ];
        }
    }
}
