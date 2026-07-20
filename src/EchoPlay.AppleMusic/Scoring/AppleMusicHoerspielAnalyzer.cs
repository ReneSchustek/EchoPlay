using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Http;
using EchoPlay.Core.Scoring;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Führt die Apple-Music-spezifische Hörspiel-Analyse durch.
    /// Kombiniert Name-Matching, Genre-Prüfung und Album-Struktur-Analyse zu einem reinen Analyse-Ergebnis.
    /// Seit dem Wechsel auf die iTunes Search API steht auch das primäre Genre zur Verfügung.
    /// </summary>
    internal sealed class AppleMusicHoerspielAnalyzer
    {
        private readonly IAppleMusicSearchClient _searchClient;
        private readonly AppleMusicHoerspielSettings _settings;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert den Analyzer mit allen benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="searchClient">Der iTunes-Search-Client für Album- und Track-Abfragen.</param>
        /// <param name="options">Die konfigurierbaren Bewertungsregeln.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public AppleMusicHoerspielAnalyzer(
            IAppleMusicSearchClient searchClient,
            IOptions<AppleMusicHoerspielSettings> options,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            _searchClient = searchClient;
            _settings = options.Value;
            _logger = loggerFactory.CreateLogger("AppleMusicHoerspielAnalyzer");
        }

        /// <summary>
        /// Analysiert einen iTunes-Künstler hinsichtlich Hörspiel-Merkmalen.
        /// </summary>
        /// <param name="source">Der iTunes-Künstler.</param>
        /// <param name="searchQuery">Ursprünglicher Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Analyse-Ergebnis mit Boolean-Flags.</returns>
        public async Task<AppleMusicHoerspielAnalysis> AnalyzeAsync(
            ITunesArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Hörspiel-Analyse für Künstler '{source.ArtistName}' (ID: {source.ArtistId}) gestartet.");

            string artistName = source.ArtistName;
            bool isKnownSeries = HoerspielNameMatcher.IsKnownSeries(artistName, _settings.DefaultKnownSeries);

            string normalizedName = HoerspielTextNormalizer.Normalize(artistName);
            string normalizedQuery = HoerspielTextNormalizer.Normalize(searchQuery);

            bool nameContainsQuery = normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            bool hasNumberVariantMatch = HoerspielNameMatcher.HasNumberVariantMatch(normalizedName, normalizedQuery, _settings.NumberWordMapping);
            bool hasExactWordMatch = HoerspielNameMatcher.IsExactWordMatch(normalizedName, normalizedQuery);
            bool hasHoerspielGenre = IsHoerspielGenre(source.PrimaryGenreName);

            (bool hasAlbums, bool hasHoerspielAlbumStructure) = await AnalyzeAlbumsAsync(source.ArtistId, cancellationToken).ConfigureAwait(false);

            DebugInfoBuilder debug = new();
            debug.Add(isKnownSeries, $"Bekannte Serie: '{artistName}'");
            debug.Add(nameContainsQuery, "Name-Contains-Match");
            debug.Add(hasNumberVariantMatch, "Zahlwort-Variante");
            debug.Add(hasExactWordMatch, "Exaktes Wort-Match");
            debug.Add(hasHoerspielGenre, $"Hörspiel-Genre: '{source.PrimaryGenreName}'");
            debug.Add(hasHoerspielAlbumStructure, "Hörspiel-Albumstruktur");
            debug.Add(!hasHoerspielAlbumStructure && !hasAlbums, "Keine Alben");

            string debugInfo = debug.Build("Keine Indikatoren gefunden");

            _logger.Debug(() => $"Hörspiel-Analyse für '{artistName}' abgeschlossen: {debugInfo}");

            return new AppleMusicHoerspielAnalysis
            {
                IsKnownSeries = isKnownSeries,
                NameContainsQuery = nameContainsQuery,
                HasNumberVariantMatch = hasNumberVariantMatch,
                HasExactWordMatch = hasExactWordMatch,
                HasHoerspielGenre = hasHoerspielGenre,
                HasHoerspielAlbumStructure = hasHoerspielAlbumStructure,
                HasAlbums = hasAlbums,
                DebugInfo = debugInfo
            };
        }

        /// <summary>
        /// Prüft, ob das primäre Genre des Künstlers auf Hörspiel-Inhalte hinweist.
        /// </summary>
        /// <param name="primaryGenreName">Das primäre Genre des Künstlers.</param>
        /// <returns><c>true</c>, wenn ein Hörspiel-typisches Genre erkannt wurde.</returns>
        private bool IsHoerspielGenre(string? primaryGenreName)
        {
            if (string.IsNullOrWhiteSpace(primaryGenreName))
            {
                return false;
            }

            foreach (string genre in _settings.HoerspielGenres)
            {
                if (string.Equals(primaryGenreName, genre, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Analysiert die Album-Struktur eines Künstlers auf Hörspiel-Merkmale.
        /// Lädt Alben und deren Tracks über die iTunes Lookup API.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Ein Tupel mit Flags: ob Alben vorhanden sind und ob Hörspiel-Struktur erkannt wurde.</returns>
        private async Task<(bool HasAlbums, bool HasHoerspielStructure)> AnalyzeAlbumsAsync(long artistId, CancellationToken cancellationToken)
        {
            ITunesResponseDto<ITunesCollectionDto> albumsResponse =
                await _searchClient.LookupAlbumsAsync(artistId, cancellationToken).ConfigureAwait(false);

            // Lookup-Antworten enthalten den Künstler als erstes Element
            List<ITunesCollectionDto> albums = albumsResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (albums.Count == 0)
            {
                return (false, false);
            }

            // Maximal AlbumsToCheck Alben prüfen
            int albumsToCheck = Math.Min(albums.Count, _settings.AlbumsToCheck);

            for (int i = 0; i < albumsToCheck; i++)
            {
                ITunesCollectionDto album = albums[i];

                ITunesResponseDto<ITunesTrackDto> tracksResponse;

                try
                {
                    tracksResponse = await _searchClient.LookupTracksAsync(album.CollectionId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (TransientRequestError.IsTransient(ex))
                {
                    // Schlägt das Laden der Tracks für ein Album fehl, wird es bei der Strukturanalyse
                    // übersprungen, um die Gesamtbewertung nicht zu blockieren.
                    _logger.Warning(
                        $"Tracks für iTunes-Album '{album.CollectionId}' ('{album.CollectionName}') konnten nicht geladen werden. Album wird bei der Hörspielanalyse übersprungen.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                // Lookup-Antworten enthalten das Album als erstes Element
                List<ITunesTrackDto> tracks = tracksResponse.Results
                    .Where(r => string.Equals(r.WrapperType, "track", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (tracks.Count == 0)
                {
                    continue;
                }

                // Trackdauern extrahieren und an Core-Heuristik delegieren
                List<TimeSpan> durations = new(tracks.Count);

                foreach (ITunesTrackDto track in tracks)
                {
                    int millis = track.TrackTimeMillis ?? 0;
                    durations.Add(TimeSpan.FromMilliseconds(millis));
                }

                if (HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations))
                {
                    return (true, true);
                }
            }

            return (true, false);
        }
    }
}
