using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IAppleMusicSearchClient"/> ohne Netzzugriff.
    /// Liefert vorgegebene Künstler und Alben und zählt die Aufrufe mit.
    /// </summary>
    internal sealed class FakeAppleMusicSearchClient : IAppleMusicSearchClient
    {
        private readonly List<ITunesArtistDto> _artists;
        private readonly Dictionary<long, List<ITunesCollectionDto>> _albumsByArtist;
        private readonly Exception? _lookupFailure;

        /// <summary>
        /// Erstellt den Fake.
        /// </summary>
        /// <param name="artists">Treffer für <see cref="SearchArtistsAsync"/>.</param>
        /// <param name="albumsByArtist">Alben je Künstler-ID für <see cref="LookupAlbumsAsync"/>.</param>
        /// <param name="lookupFailure">Wird von <see cref="LookupAlbumsAsync"/> geworfen, wenn gesetzt.</param>
        public FakeAppleMusicSearchClient(
            IEnumerable<ITunesArtistDto>? artists = null,
            IReadOnlyDictionary<long, List<ITunesCollectionDto>>? albumsByArtist = null,
            Exception? lookupFailure = null)
        {
            _artists = artists is null ? [] : [.. artists];
            _albumsByArtist = albumsByArtist is null ? [] : new(albumsByArtist);
            _lookupFailure = lookupFailure;
        }

        /// <summary>Anzahl der Künstler-Suchen – zeigt, ob die Artist-ID neu ermittelt wurde.</summary>
        public int SearchArtistsCallCount { get; private set; }

        /// <summary>Anzahl der Album-Abfragen.</summary>
        public int LookupAlbumsCallCount { get; private set; }

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(
            string query, int limit = 25, CancellationToken ct = default)
        {
            SearchArtistsCallCount++;
            return Task.FromResult(new ITunesResponseDto<ITunesArtistDto>
            {
                ResultCount = _artists.Count,
                Results = _artists
            });
        }

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(
            string query, int limit = 25, CancellationToken ct = default) =>
            Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>());

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(
            long artistId, CancellationToken ct = default)
        {
            LookupAlbumsCallCount++;

            if (_lookupFailure is not null)
            {
                throw _lookupFailure;
            }

            List<ITunesCollectionDto> albums = _albumsByArtist.TryGetValue(artistId, out List<ITunesCollectionDto>? found)
                ? found
                : [];

            return Task.FromResult(new ITunesResponseDto<ITunesCollectionDto>
            {
                ResultCount = albums.Count,
                Results = albums
            });
        }

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(
            long collectionId, CancellationToken ct = default) =>
            Task.FromResult(new ITunesResponseDto<ITunesTrackDto>());

        /// <inheritdoc/>
        public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(
            IReadOnlyList<long> collectionIds, CancellationToken ct = default) =>
            Task.FromResult(new ITunesResponseDto<ITunesTrackDto>());
    }
}
