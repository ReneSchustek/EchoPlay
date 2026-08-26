using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
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
        /// <remarks>
        /// Steht der Name bereits für eine bekannte Hörspielserie, endet die Analyse dort:
        /// Die Bewertung nimmt solche Künstler ohnehin hart an
        /// (<see cref="HoerspielScoreCalculator.Evaluate"/>), jede Albenprüfung wäre für die
        /// Tonne — und kostet vier Anfragen an der Ratenbremse.
        /// </remarks>
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
            bool hasHoerspielGenre = AppleMusicGenreMatcher.IsHoerspielGenre(source.PrimaryGenreName, _settings.HoerspielGenres);

            AlbumAnalysis albums = isKnownSeries
                ? AlbumAnalysis.Skipped
                : await AnalyzeAlbumsAsync(source.ArtistId, cancellationToken).ConfigureAwait(false);

            DebugInfoBuilder debug = new();
            debug.Add(isKnownSeries, $"Bekannte Serie: '{artistName}'");
            debug.Add(nameContainsQuery, "Name-Contains-Match");
            debug.Add(hasNumberVariantMatch, "Zahlwort-Variante");
            debug.Add(hasExactWordMatch, "Exaktes Wort-Match");
            debug.Add(hasHoerspielGenre, $"Hörspiel-Genre: '{source.PrimaryGenreName}'");
            debug.Add(albums.HasHoerspielStructure, "Hörspiel-Albumstruktur");
            debug.Add(!isKnownSeries && !albums.HasAlbums, "Keine Alben");
            debug.Add(isKnownSeries, "Albenprüfung übersprungen");

            string debugInfo = debug.Build("Keine Indikatoren gefunden");

            _logger.Debug(() => $"Hörspiel-Analyse für '{artistName}' abgeschlossen: {debugInfo}");

            return new AppleMusicHoerspielAnalysis
            {
                IsKnownSeries = isKnownSeries,
                NameContainsQuery = nameContainsQuery,
                HasNumberVariantMatch = hasNumberVariantMatch,
                HasExactWordMatch = hasExactWordMatch,
                HasHoerspielGenre = hasHoerspielGenre,
                HasHoerspielAlbumStructure = albums.HasHoerspielStructure,
                HasAlbums = albums.HasAlbums,
                ArtworkUrl = albums.ArtworkUrl,
                DebugInfo = debugInfo
            };
        }

        /// <summary>
        /// Das Ergebnis der Albenprüfung.
        /// </summary>
        /// <param name="HasAlbums">Ob der Künstler überhaupt Alben besitzt.</param>
        /// <param name="HasHoerspielStructure">Ob mindestens ein Album die Hörspiel-Struktur zeigt.</param>
        /// <param name="ArtworkUrl">
        /// Die Cover-Adresse des ersten Albums. Sie ist das Cover der Serie — auf Künstlerebene
        /// liefert der Anbieter keines, auf Albenebene schon, und die Liste liegt hier ohnehin vor.
        /// </param>
        private readonly record struct AlbumAnalysis(bool HasAlbums, bool HasHoerspielStructure, string? ArtworkUrl)
        {
            /// <summary>Das Ergebnis, wenn die Prüfung übersprungen wurde.</summary>
            public static AlbumAnalysis Skipped => new(HasAlbums: false, HasHoerspielStructure: false, ArtworkUrl: null);
        }

        /// <summary>
        /// Analysiert die Album-Struktur eines Künstlers auf Hörspiel-Merkmale.
        /// Lädt die Alben und die Titel der ersten Alben über die iTunes Lookup API.
        /// </summary>
        /// <remarks>
        /// Die Titel aller zu prüfenden Alben kommen in einer einzigen Anfrage
        /// (<see cref="IAppleMusicSearchClient.LookupTracksBatchAsync"/>): Die Lookup-API nimmt
        /// mehrere Kennungen entgegen, und jede eingesparte Anfrage ist an der Ratenbremse
        /// anderthalb Sekunden wert.
        /// </remarks>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Ergebnis der Albenprüfung.</returns>
        private async Task<AlbumAnalysis> AnalyzeAlbumsAsync(long artistId, CancellationToken cancellationToken)
        {
            ITunesResponseDto<ITunesCollectionDto> albumsResponse =
                await _searchClient.LookupAlbumsAsync(artistId, cancellationToken).ConfigureAwait(false);

            // Lookup-Antworten enthalten den Künstler als erstes Element
            List<ITunesCollectionDto> albums = albumsResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (albums.Count == 0)
            {
                return new AlbumAnalysis(HasAlbums: false, HasHoerspielStructure: false, ArtworkUrl: null);
            }

            string? artworkUrl = AppleMusicArtworkUrl.WithSize(albums[0].ArtworkUrl100);

            // Maximal AlbumsToCheck Alben prüfen
            int albumsToCheck = Math.Min(albums.Count, _settings.AlbumsToCheck);
            List<long> collectionIds = albums.Take(albumsToCheck).Select(album => album.CollectionId).ToList();

            IReadOnlyDictionary<long, List<TimeSpan>>? durationsByAlbum =
                await LoadTrackDurationsAsync(collectionIds, cancellationToken).ConfigureAwait(false);

            if (durationsByAlbum is null)
            {
                return new AlbumAnalysis(HasAlbums: true, HasHoerspielStructure: false, artworkUrl);
            }

            foreach (long collectionId in collectionIds)
            {
                if (durationsByAlbum.TryGetValue(collectionId, out List<TimeSpan>? durations)
                    && durations.Count > 0
                    && HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations))
                {
                    return new AlbumAnalysis(HasAlbums: true, HasHoerspielStructure: true, artworkUrl);
                }
            }

            return new AlbumAnalysis(HasAlbums: true, HasHoerspielStructure: false, artworkUrl);
        }

        /// <summary>
        /// Lädt die Titeldauern der angegebenen Alben in einer Anfrage und ordnet sie ihrem Album zu.
        /// </summary>
        /// <param name="collectionIds">Die Kennungen der zu prüfenden Alben.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>
        /// Die Dauern je Album, oder <see langword="null"/>, wenn die Gegenstelle die Anfrage
        /// vorübergehend nicht beantworten konnte. Dann bleibt die Strukturfrage offen —
        /// die Bewertung stützt sich auf Name und Genre.
        /// </returns>
        private async Task<IReadOnlyDictionary<long, List<TimeSpan>>?> LoadTrackDurationsAsync(
            List<long> collectionIds,
            CancellationToken cancellationToken)
        {
            ITunesResponseDto<ITunesTrackDto> tracksResponse;

            try
            {
                tracksResponse = await _searchClient.LookupTracksBatchAsync(collectionIds, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (TransientRequestError.IsTransient(ex, cancellationToken))
            {
                // Schlägt das Laden der Titel fehl, entfällt die Strukturanalyse; die Bewertung
                // läuft mit den übrigen Merkmalen weiter, statt den Künstler ganz zu verlieren.
                _logger.Warning(
                    $"Titel der Alben [{string.Join(", ", collectionIds)}] konnten nicht geladen werden. Die Albenstruktur bleibt bei der Hörspielanalyse unberücksichtigt.");
                _logger.Error("Fehlerdetails:", ex);
                return null;
            }

            Dictionary<long, List<TimeSpan>> durationsByAlbum = new(collectionIds.Count);

            foreach (ITunesTrackDto track in tracksResponse.Results)
            {
                // Lookup-Antworten enthalten die Alben selbst als eigene Einträge
                if (!string.Equals(track.WrapperType, "track", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!durationsByAlbum.TryGetValue(track.CollectionId, out List<TimeSpan>? durations))
                {
                    durations = [];
                    durationsByAlbum[track.CollectionId] = durations;
                }

                durations.Add(TimeSpan.FromMilliseconds(track.TrackTimeMillis ?? 0));
            }

            return durationsByAlbum;
        }
    }
}
