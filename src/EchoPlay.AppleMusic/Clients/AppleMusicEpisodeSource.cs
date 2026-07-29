using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Http;
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
        /// <param name="knownEpisodeTitles">Bereits bekannte Episoden-Titel; für passende Alben entfällt der Track-Lookup, ihre Metadaten inkl. Cover werden dennoch geliefert (siehe Interface). Null/leer lädt alle vollständig.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine nach Erscheinungsdatum absteigend sortierte Liste importierbarer Episoden.</returns>
        public async Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
            string sourceSeriesId,
            IReadOnlySet<string>? knownEpisodeTitles = null,
            CancellationToken cancellationToken = default)
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

            _logger.Debug(() => $"Apple-Music-Episodenimport gestartet für Künstler '{sourceSeriesId}'.");

            ITunesResponseDto<ITunesCollectionDto> albumsResponse =
                await _searchClient.LookupAlbumsAsync(artistId, cancellationToken).ConfigureAwait(false);

            // Lookup-Antworten enthalten neben den eigenen Alben gelegentlich auch
            // Compilation-/Various-Artists-Einträge mit fremder ArtistId (z. B. Sammlungen,
            // bei denen der gesuchte Künstler nur als Featured-Beitrag auftaucht). Ohne den
            // strikten ArtistId-Filter würden deren Tracks als Episoden der gesuchten Serie
            // importiert. Wir behalten daher nur Alben, deren ArtistId exakt der angefragten
            // Lookup-ID entspricht.
            // Alben neueste-zuerst verarbeiten (ReleaseDate ist ein ISO-8601-String, lexikalisch
            // = chronologisch sortierbar; datumslose Sonderausgaben landen ans Ende). Der
            // anschließende Track-Lookup läuft pro Album sequenziell und rate-limitiert; wird der
            // Import unterbrochen (App-Shutdown → TaskCanceledException), sind so wenigstens die
            // NEUESTEN Folgen bereits importiert, statt dass genau sie als Letzte wegfallen.
            List<ITunesCollectionDto> albums = albumsResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase)
                            && r.ArtistId == artistId)
                .OrderByDescending(r => r.ReleaseDate, StringComparer.Ordinal)
                .ToList();

            int foreignAlbums = albumsResponse.Results
                .Count(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase)
                            && r.ArtistId != artistId);

            if (foreignAlbums > 0)
            {
                _logger.Warning(
                    "Apple-Music-Lookup für Künstler '{ArtistId}' enthielt {ForeignAlbumCount} fremde Alben (ArtistId weicht ab) – wurden ausgefiltert.",
                    sourceSeriesId, foreignAlbums);
            }

            if (albums.Count == 0)
            {
                _logger.Debug(() => $"Keine Alben für Künstler '{sourceSeriesId}' gefunden.");
                return [];
            }

            // Track-Lookups gebündelt: Ein Album = eine Folge, die Tracks dienen NUR der
            // Dauerberechnung. Statt pro Album einen eigenen, host-rate-limitierten Lookup
            // (je >= 1,5 s → bei 230 Folgen ~6 min) werden mehrere Collection-IDs in einem
            // Lookup-Request gebündelt. Die iTunes-Lookup-API akzeptiert kommaseparierte IDs;
            // jeder Track trägt seine CollectionId zur Zuordnung. So kollabieren N rate-limitierte
            // Requests zu ~N/100 – der Import dauert Sekunden statt Minuten.
            //
            // Delta-Abgleich: Für bereits bekannte Folgen (Titel = Albumname) entfällt
            // der Track-Lookup; ihre Album-Metadaten (inkl. Cover-URL) kommen dennoch durch, damit
            // ein fehlendes Cover einer bestehenden Folge nachgetragen werden kann. Ohne diese
            // Ersparnis kostet jeder Neu-Folgen-Check einen Track-Lookup pro vorhandener Folge.
            List<ITunesCollectionDto> albumsNeedingTracks = albums
                .Where(a => !(knownEpisodeTitles is { Count: > 0 } && knownEpisodeTitles.Contains(a.CollectionName)))
                .ToList();

            Dictionary<long, List<ITunesTrackDto>> tracksByCollection = [];
            const int trackLookupBatchSize = 100;

            foreach (ITunesCollectionDto[] batch in albumsNeedingTracks.Chunk(trackLookupBatchSize))
            {
                long[] batchIds = [.. batch.Select(a => a.CollectionId)];
                ITunesResponseDto<ITunesTrackDto> tracksResponse;

                try
                {
                    tracksResponse = await _searchClient.LookupTracksBatchAsync(batchIds, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (TransientRequestError.IsTransient(ex))
                {
                    // Batch-Fehler unterbricht den Import nicht: die betroffenen Folgen werden ohne
                    // Dauer importiert (kein Datenverlust), statt komplett zu fehlen.
                    _logger.Warning(
                        $"Track-Batch-Lookup für {batchIds.Length} iTunes-Alben fehlgeschlagen – Folgen werden ohne Dauer importiert.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                // Lookup-Antworten enthalten je Album auch dessen Collection-Eintrag – nur Tracks
                // interessieren, zugeordnet über die CollectionId des Tracks.
                foreach (ITunesTrackDto track in tracksResponse.Results)
                {
                    if (!string.Equals(track.WrapperType, "track", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!tracksByCollection.TryGetValue(track.CollectionId, out List<ITunesTrackDto>? list))
                    {
                        list = [];
                        tracksByCollection[track.CollectionId] = list;
                    }

                    list.Add(track);
                }
            }

            List<ImportEpisode> episodes = new(albums.Count);
            int orderIndex = 0;

            foreach (ITunesCollectionDto album in albums)
            {
                // Bekannte Folgen wurden nicht gebatcht → leere Trackliste, Dauer bleibt wie bisher 0.
                IReadOnlyList<ITunesTrackDto> tracks =
                    tracksByCollection.TryGetValue(album.CollectionId, out List<ITunesTrackDto>? found) ? found : [];

                // Ein Album = eine Folge – Tracks werden nur für die Dauerberechnung verwendet
                ImportEpisode episode = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex);
                episodes.Add(episode);
                orderIndex++;
            }

            _logger.Info(
                "Apple-Music-Episodenimport abgeschlossen: {EpisodeCount} Episode(n) für Künstler '{ArtistId}'.",
                episodes.Count, sourceSeriesId);

            return episodes;
        }
    }
}
