using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Http;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;

namespace EchoPlay.Spotify.Services
{
    /// <summary>
    /// Spotify-spezifische Implementierung des Episodenimports für eine Hörspielserie.
    /// Die Klasse lädt alle relevanten Alben und Tracks und übersetzt diese in fachlich sortierte Import-Episoden.
    /// </summary>
    /// <remarks>
    /// Initialisiert die Episoden-Importquelle mit dem Spotify-API-Client.
    /// </remarks>
    /// <param name="apiClient">Der Spotify-API-Client.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    internal sealed class SpotifyEpisodeImportSource(
        ISpotifyApiClient apiClient,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IEpisodeImportSource
    {
        private readonly ISpotifyApiClient _apiClient = apiClient;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("SpotifyEpisodeImportSource");

        /// <summary>
        /// Lädt alle Episoden zu einer importierbaren Serie und stellt diese in stabiler Reihenfolge bereit.
        /// Schlägt das Laden der Tracks für ein einzelnes Album fehl, wird dieses übersprungen –
        /// die Episoden der übrigen Alben bleiben unberührt.
        /// </summary>
        /// <param name="sourceSeriesId">Die Spotify-Artist-ID der Serie.</param>
        /// <param name="knownEpisodeTitles">Bereits bekannte Episoden-Titel; für passende Alben entfällt der Track-Lookup, ihre Metadaten inkl. Cover werden dennoch geliefert (siehe Interface). Null/leer lädt alle vollständig.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine nach Erscheinungsdatum absteigend sortierte Liste importierbarer Episoden.</returns>
        public async Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
            string sourceSeriesId,
            IReadOnlySet<string>? knownEpisodeTitles = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceSeriesId);

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"Import:Spotify:{sourceSeriesId}");

            _logger.Debug(() => $"Spotify-Episodenimport gestartet für Künstler '{sourceSeriesId}'.");

            // Kein künstliches Limit – alle Alben laden, auch bei Serien mit 200+ Folgen.
            // Die Pagination im ApiClient iteriert seitenweise (je 50) bis alle geladen sind.
            IReadOnlyList<SpotifyAlbumDto> albums = await _apiClient.GetArtistAlbumsAsync(sourceSeriesId, limit: int.MaxValue, cancellationToken).ConfigureAwait(false);

            // Neueste zuerst – bei einem abgebrochenen Import (App-Shutdown) liegen so die
            // aktuellsten Folgen bereits vor, statt als Letzte wegzufallen.
            IEnumerable<SpotifyAlbumDto> orderedAlbums = albums.OrderByDescending(a => a.ReleaseDate);

            List<ImportEpisode> episodes = [];
            int orderIndex = 0;

            foreach (SpotifyAlbumDto album in orderedAlbums)
            {
                // Delta-Abgleich: Bei bereits bekannten Folgen (Titel = Albumname) entfällt der
                // Track-Lookup. Er dient nur der Dauerberechnung und ist für bestehende Folgen
                // unnötig. Die Album-Metadaten (inkl. Cover-URL) werden trotzdem geliefert, damit
                // der Delta-Import bei einer bestehenden Folge ein fehlendes Cover nachtragen kann.
                // Ohne diese Ersparnis kostet jeder Neu-Folgen-Check einen Track-Lookup pro
                // bestehender Folge.
                bool isKnown = knownEpisodeTitles is { Count: > 0 }
                    && knownEpisodeTitles.Contains(album.Title);

                IReadOnlyList<SpotifyTrackDto> tracks;

                if (isKnown)
                {
                    tracks = [];
                }
                else
                {
                    try
                    {
                        tracks = await _apiClient.GetAlbumTracksAsync(album.SpotifyAlbumId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (TransientRequestError.IsTransient(ex))
                    {
                        // Einzelne Album-Fehler dürfen den Gesamtimport nicht unterbrechen.
                        _logger.Warning(
                            $"Tracks für Spotify-Album '{album.SpotifyAlbumId}' ('{album.Title}') konnten nicht geladen werden. Album wird übersprungen.");
                        _logger.Error("Fehlerdetails:", ex);
                        continue;
                    }
                }

                // Ein Album = eine Folge – Tracks werden nur für die Dauerberechnung verwendet
                ImportEpisode episode = SpotifyEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex);
                episodes.Add(episode);
                orderIndex++;
            }

            _logger.Info(
                "Spotify-Episodenimport abgeschlossen: {EpisodeCount} Episode(n) für Künstler '{ArtistId}'.",
                episodes.Count, sourceSeriesId);

            return episodes;
        }
    }
}
