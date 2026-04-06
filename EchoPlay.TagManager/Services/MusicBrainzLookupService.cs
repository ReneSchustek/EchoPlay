using System.Net.Http.Json;
using EchoPlay.Logger.Abstractions;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services.Dtos;

namespace EchoPlay.TagManager.Services
{
    /// <summary>
    /// Sucht Metadaten über die MusicBrainz-REST-API.
    /// MusicBrainz ist eine freie, gemeinschaftlich gepflegte Musikdatenbank –
    /// kein API-Key notwendig, aber die Nutzungsbedingungen fordern einen eindeutigen User-Agent
    /// und erlauben maximal eine Anfrage pro Sekunde für anonyme Requests.
    /// </summary>
    internal sealed class MusicBrainzLookupService : ITagLookupService
    {
        /// <summary>
        /// Stellt sicher, dass nie zwei Anfragen gleichzeitig abgesetzt werden.
        /// SemaphoreSlim(1,1) wirkt wie ein asynchrones Lock ohne Threadbindung.
        /// Static, damit auch mehrere Instanzen die Rate gemeinsam einhalten.
        /// </summary>
        private static readonly SemaphoreSlim RateLimitSemaphore = new(1, 1);

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den <see cref="MusicBrainzLookupService"/>.
        /// Der <see cref="HttpClient"/> wird von außen injiziert, damit Base-URL
        /// und User-Agent-Header zentral über <c>AddTagManager()</c> konfiguriert werden.
        /// </summary>
        /// <param name="httpClient">Vorkonfigurierter HTTP-Client für MusicBrainz.</param>
        /// <param name="loggerFactory">Factory, die den Logger für diesen Dienst erstellt.</param>
        public MusicBrainzLookupService(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _logger     = loggerFactory.CreateLogger(nameof(MusicBrainzLookupService));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            // MusicBrainz Rate-Limiting: maximal 1 Request/Sekunde.
            // Das Semaphor serialisiert alle Anfragen und die anschließende Wartezeit
            // verhindert, dass der nächste Aufrufer sofort loslegt.
            await RateLimitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // 1 Sekunde warten, um die Rate-Limit-Regel einzuhalten.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                // MusicBrainz Lucene-Syntax: release:<term> sucht im Titelfeldfeld,
                // ohne Feldpräfix wird in allen Feldern gesucht.
                string encodedQuery = Uri.EscapeDataString(query);
                string requestUri   = $"ws/2/release?query={encodedQuery}&fmt=json&limit=5";

                _logger.Debug($"MusicBrainz-Suche: {requestUri}");

                using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                MusicBrainzReleaseSearchResponse? dto = await response.Content
                    .ReadFromJsonAsync<MusicBrainzReleaseSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

                if (dto is null || dto.Releases.Count == 0)
                {
                    _logger.Info($"Keine Ergebnisse für Suche: {query}");
                    return [];
                }

                List<TagLookupResult> results = dto.Releases
                    .Select(MapToTagLookupResult)
                    .ToList();

                _logger.Info($"MusicBrainz: {results.Count} Ergebnisse für \"{query}\"");
                return results;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"MusicBrainz nicht erreichbar (Suche: {query})", ex);
                throw;
            }
            finally
            {
                RateLimitSemaphore.Release();
            }
        }

        // --- Hilfsmethoden ---

        /// <summary>
        /// Wandelt ein <see cref="MusicBrainzRelease"/>-DTO in ein <see cref="TagLookupResult"/> um.
        /// Das Erscheinungsjahr wird aus dem <c>date</c>-Feld extrahiert –
        /// MusicBrainz liefert Datumsangaben unterschiedlicher Genauigkeit ("2023", "2023-05-01").
        /// </summary>
        private static TagLookupResult MapToTagLookupResult(MusicBrainzRelease release)
        {
            // Ersten Künstler aus dem Artist-Credit-Array entnehmen
            string? artist = release.ArtistCredits.Count > 0
                ? release.ArtistCredits[0].Artist?.Name
                : null;

            // Nur das Jahr extrahieren – die ersten 4 Zeichen des date-Felds
            uint? year = null;
            if (!string.IsNullOrEmpty(release.Date) && release.Date.Length >= 4)
            {
                if (uint.TryParse(release.Date[..4], out uint parsedYear))
                {
                    year = parsedYear;
                }
            }

            return new TagLookupResult
            {
                Title      = release.Title,
                Artist     = artist,
                Album      = release.Title,
                Year       = year,
                TrackCount = release.TrackCount == 0 ? null : release.TrackCount,
                Source     = "MusicBrainz"
            };
        }
    }
}
