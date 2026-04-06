using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;

namespace EchoPlay.AppleMusic.Clients
{
    /// <summary>
    /// Lädt und aggregiert Episoden einer Hörspielserie über die iTunes Lookup API.
    /// Die SourceSeriesId entspricht der iTunes-Artist-ID.
    /// </summary>
    public sealed class AppleMusicEpisodeSource : IEpisodeImportSource
    {
        private readonly IAppleMusicSearchClient _searchClient;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert die Episodenquelle mit dem Search-Client.
        /// </summary>
        /// <param name="searchClient">Der iTunes-Search-Client für Album- und Track-Lookups.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public AppleMusicEpisodeSource(
            IAppleMusicSearchClient searchClient,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(searchClient);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _searchClient = searchClient;
            _logger = loggerFactory.CreateLogger("AppleMusicEpisodeSource");
        }

        /// <summary>
        /// Lädt alle Episoden zu einer importierbaren Serie.
        /// Die Methode lädt zunächst alle Alben des Künstlers und anschließend
        /// die Tracks jedes Albums über die iTunes Lookup API.
        /// Schlägt das Laden der Tracks für ein einzelnes Album fehl, wird dieses übersprungen –
        /// die Episoden der übrigen Alben bleiben unberührt.
        /// </summary>
        /// <param name="sourceSeriesId">Die iTunes-Artist-ID als String.</param>
        /// <returns>Eine sortierte Liste importierbarer Episoden.</returns>
        public async Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(string sourceSeriesId)
        {
            if (string.IsNullOrWhiteSpace(sourceSeriesId))
            {
                throw new ArgumentException("SourceSeriesId darf nicht leer sein.", nameof(sourceSeriesId));
            }

            if (!long.TryParse(sourceSeriesId, out long artistId))
            {
                throw new ArgumentException("SourceSeriesId muss eine gültige iTunes-Artist-ID sein.", nameof(sourceSeriesId));
            }

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"Import:AppleMusic:{sourceSeriesId}");

            _logger.Debug($"Apple-Music-Episodenimport gestartet für Künstler '{sourceSeriesId}'.");

            ITunesResponseDto<ITunesCollectionDto> albumsResponse =
                await _searchClient.LookupAlbumsAsync(artistId).ConfigureAwait(false);

            // Lookup-Antworten enthalten den Künstler als erstes Element
            List<ITunesCollectionDto> albums = albumsResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (albums.Count == 0)
            {
                _logger.Debug($"Keine Alben für Künstler '{sourceSeriesId}' gefunden.");
                return [];
            }

            List<ImportEpisode> episodes = new();
            int orderIndex = 0;

            foreach (ITunesCollectionDto album in albums)
            {
                ITunesResponseDto<ITunesTrackDto> tracksResponse;

                try
                {
                    tracksResponse = await _searchClient.LookupTracksAsync(album.CollectionId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Einzelne Album-Fehler dürfen den Gesamtimport nicht unterbrechen.
                    _logger.Warning(
                        $"Tracks für iTunes-Album '{album.CollectionId}' ('{album.CollectionName}') konnten nicht geladen werden. Album wird übersprungen.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                // Lookup-Antworten enthalten das Album als erstes Element
                List<ITunesTrackDto> tracks = tracksResponse.Results
                    .Where(r => string.Equals(r.WrapperType, "track", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Ein Album = eine Folge – Tracks werden nur für die Dauerberechnung verwendet
                ImportEpisode episode = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex);
                episodes.Add(episode);
                orderIndex++;
            }

            _logger.Info($"Apple-Music-Episodenimport abgeschlossen: {episodes.Count} Episode(n) für Künstler '{sourceSeriesId}'.");

            return episodes;
        }
    }
}
