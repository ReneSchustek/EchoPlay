using EchoPlay.Core.Abstractions.Time;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Prüft die übergebenen Serien gegen die iTunes API auf neue Folgen.
    /// Vergleicht die höchste online verfügbare Episodennummer mit der höchsten
    /// lokal vorhandenen Nummer (aus den Ordnernamen im Serienverzeichnis).
    /// </summary>
    /// <remarks>
    /// Ablauf pro Serie:
    /// 1. Apple Music Artist ID ermitteln (aus DB oder per Namenssuche)
    /// 2. Alle Alben des Künstlers über iTunes Lookup API laden
    /// 3. Episodennummern aus Albumnamen extrahieren (Regex)
    /// 4. Lokale Ordner scannen und höchste Nummer bestimmen
    /// 5. Differenz berechnen + angekündigte Folgen erkennen
    /// </remarks>
    internal sealed class OnlineEpisodeChecker : IOnlineEpisodeChecker
    {
        private readonly IAppleMusicSearchClient _appleMusicClient;
        private readonly ISeriesDataService _seriesDataService;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;
        private readonly IClock _clock;

        /// <summary>
        /// Initialisiert den Checker mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="appleMusicClient">Client für die iTunes Search API.</param>
        /// <param name="seriesDataService">Datenzugriff für Serien (zum Speichern der Apple Music Artist ID).</param>
        /// <param name="loggerFactory">Logger-Factory für diagnostische Ausgaben.</param>
        /// <param name="clock">Zeitquelle für Zeitstempel.</param>
        public OnlineEpisodeChecker(
            IAppleMusicSearchClient appleMusicClient,
            ISeriesDataService seriesDataService,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory,
            IClock clock)
        {
            _appleMusicClient = appleMusicClient;
            _seriesDataService = seriesDataService;
            _logger = loggerFactory.CreateLogger("OnlineEpisodeChecker");
            _clock = clock;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            return CheckSeriesSequentiallyAsync(
                subscribedSeries,
                (series, ct) => CheckSingleSeriesAsync(series, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            DateTime cutoffDate,
            CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.OnlineEpisodeCheck);
            return await CheckSeriesSequentiallyAsync(
                subscribedSeries,
                (series, ct) => CheckNewReleasesForSeriesAsync(series, cutoffDate, ct),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gemeinsame Schleife für die Serienprüfung, eine Serie nach der anderen. Fehler bei
        /// einer Serie blockieren die restlichen nicht.
        /// </summary>
        /// <remarks>
        /// Eine eigene Pause zwischen den Serien braucht es nicht: Der HTTP-Client für iTunes
        /// läuft über die Ratenbremse aus <c>AppServiceRegistration</c>, die zwischen zwei
        /// Anfragen an itunes.apple.com 1,5 Sekunden einhält, auch über Seriengrenzen hinweg.
        /// </remarks>
        /// <param name="subscribedSeries">Zu prüfende Serien.</param>
        /// <param name="checkFunc">Prüflogik pro Serie (CheckSingle oder CheckNewReleases).</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Ergebnisse aller erfolgreich geprüften Serien.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sequenzieller Mehr-Serien-Check: HTTP-/Provider-Fehler einer einzelnen Serie werden geloggt und übersprungen, damit die restlichen Serien weiterhin geprüft werden. Der Aufrufer erhält die Liste der erfolgreichen Ergebnisse.")]
        private async Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckSeriesSequentiallyAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            Func<CheckableSeriesInfo, CancellationToken, Task<OnlineEpisodeCheckResult?>> checkFunc,
            CancellationToken cancellationToken)
        {
            List<OnlineEpisodeCheckResult> results = [];

            foreach (CheckableSeriesInfo series in subscribedSeries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    OnlineEpisodeCheckResult? result = await checkFunc(series, cancellationToken);

                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Ein Abbruch des Aufrufers beendet den Durchlauf. Eine Zeitüberschreitung
                    // bei iTunes dagegen trifft nur diese Serie und fällt in den Zweig darunter.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Prüfung für '{SeriesTitle}' fehlgeschlagen: {Reason}", series.Title, ex.Message);
                }
            }

            return results;
        }

        /// <summary>
        /// Prüft eine einzelne Serie auf Neuerscheinungen im Zeitfenster.
        /// Lädt alle Alben des Künstlers und filtert auf Alben mit ReleaseDate ab dem Cutoff.
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="cutoffDate">Ältestes erlaubtes Veröffentlichungsdatum (UTC).</param>
        /// <param name="ct">Abbruchtoken für die API-Aufrufe.</param>
        /// <returns>Prüfergebnis mit befüllten NewReleaseEpisodes, oder null wenn keine Artist-ID ermittelbar.</returns>
        private async Task<OnlineEpisodeCheckResult?> CheckNewReleasesForSeriesAsync(
            CheckableSeriesInfo series,
            DateTime cutoffDate,
            CancellationToken ct = default)
        {
            List<ITunesCollectionDto>? albums = await LoadAlbumsAsync(series, ct);
            if (albums is null)
            {
                return null;
            }

            (List<NewReleaseEpisode> newReleases, List<AnnouncedEpisode> announced) =
                ITunesAlbumEvaluation.CollectNewReleases(albums, cutoffDate, _clock.UtcNow.Date);

            // Keine relevanten Ergebnisse? Serie nicht ins Ergebnis aufnehmen.
            if (newReleases.Count == 0 && announced.Count == 0)
            {
                return null;
            }

            // Lokalen Stand ermitteln (für Höchstnummer-Vergleich)
            int localHighest = GetLocalHighestEpisodeNumber(series.LocalFolderPath, _logger);
            int onlineHighest = HighestNumber(newReleases);

            return new OnlineEpisodeCheckResult
            {
                SeriesId = series.SeriesId,
                SeriesTitle = series.Title,
                OnlineHighestNumber = onlineHighest,
                LocalHighestNumber = localHighest,
                NewEpisodesCount = newReleases.Count,
                AnnouncedEpisodes = announced,
                NewReleaseEpisodes = newReleases,
                CoverUrl = series.CoverImageUrl,
                CheckedAtUtc = _clock.UtcNow
            };
        }

        /// <summary>Die höchste Folgennummer unter den Neuerscheinungen, 0 ohne nummerierte.</summary>
        private static int HighestNumber(List<NewReleaseEpisode> newReleases) => newReleases
            .Where(e => e.EpisodeNumber.HasValue)
            .Select(e => e.EpisodeNumber!.Value) // nach dem Filter oben nie null
            .DefaultIfEmpty(0)
            .Max();

        /// <summary>
        /// Prüft eine einzelne Serie gegen die iTunes API: Welche Nummern gibt es beim Anbieter,
        /// die lokal noch fehlen?
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="ct">Abbruchtoken für die API-Aufrufe.</param>
        /// <returns>Das Prüfergebnis, oder null wenn keine Artist ID ermittelt werden konnte.</returns>
        private async Task<OnlineEpisodeCheckResult?> CheckSingleSeriesAsync(
            CheckableSeriesInfo series, CancellationToken ct = default)
        {
            List<ITunesCollectionDto>? albums = await LoadAlbumsAsync(series, ct);
            if (albums is null)
            {
                return null;
            }

            int localHighest = GetLocalHighestEpisodeNumber(series.LocalFolderPath, _logger);
            (int onlineHighest, List<AnnouncedEpisode> announced, List<MissingOnlineEpisode> missingOnline) =
                ITunesAlbumEvaluation.CollectMissing(albums, localHighest, _clock.UtcNow.Date);

            return new OnlineEpisodeCheckResult
            {
                SeriesId = series.SeriesId,
                SeriesTitle = series.Title,
                OnlineHighestNumber = onlineHighest,
                LocalHighestNumber = localHighest,
                NewEpisodesCount = Math.Max(0, onlineHighest - localHighest),
                AnnouncedEpisodes = announced,
                MissingOnlineEpisodes = missingOnline,
                CoverUrl = series.CoverImageUrl,
                CheckedAtUtc = _clock.UtcNow
            };
        }

        /// <summary>
        /// Lädt die Alben des Künstlers einer Serie.
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="ct">Abbruchtoken für die API-Aufrufe.</param>
        /// <returns>Die Alben, oder null ohne Künstler-Kennung oder ohne Alben.</returns>
        private async Task<List<ITunesCollectionDto>?> LoadAlbumsAsync(CheckableSeriesInfo series, CancellationToken ct)
        {
            long? artistId = await ResolveArtistIdAsync(series, ct);
            if (artistId is null)
            {
                _logger.Debug(() => $"Keine iTunes-Artist-ID für '{series.Title}' gefunden – übersprungen.");
                return null;
            }

            ITunesResponseDto<ITunesCollectionDto> albumResponse = await _appleMusicClient
                .LookupAlbumsAsync(artistId.Value, ct);

            List<ITunesCollectionDto> albums = [.. albumResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))];

            return albums.Count == 0 ? null : albums;
        }

        /// <summary>
        /// Ermittelt die Apple Music Artist ID für eine Serie.
        /// Prüft zuerst, ob die ID bereits bekannt ist. Falls nicht, wird per Namenssuche
        /// bei iTunes gesucht und die gefundene ID in der Datenbank gespeichert.
        /// </summary>
        private async Task<long?> ResolveArtistIdAsync(
            CheckableSeriesInfo series, CancellationToken ct = default)
        {
            // Bereits bekannte ID verwenden
            if (!string.IsNullOrWhiteSpace(series.AppleMusicArtistId)
                && long.TryParse(series.AppleMusicArtistId, out long knownId))
            {
                return knownId;
            }

            // Per Namenssuche bei iTunes nach dem Künstler suchen
            _logger.Debug(() => $"Suche iTunes-Artist-ID für '{series.Title}' per Namenssuche...");

            ITunesResponseDto<ITunesArtistDto> searchResponse = await _appleMusicClient
                .SearchArtistsAsync(series.Title, limit: 5, ct);

            // Den besten Treffer nehmen, idealerweise exakte Übereinstimmung im Namen
            IReadOnlyList<ITunesArtistDto> candidates = searchResponse.Results;
            ITunesArtistDto? exactMatch = candidates
                .FirstOrDefault(a => string.Equals(a.ArtistName, series.Title, StringComparison.OrdinalIgnoreCase));
            ITunesArtistDto? bestMatch = exactMatch
                ?? (!series.RequireExactArtistName && candidates.Count > 0 ? candidates[0] : null);

            if (bestMatch is null)
            {
                return null;
            }

            // Gefundene ID in der Datenbank persistieren, damit beim nächsten Check kein Suchaufruf nötig ist
            Data.Entities.Library.Series? dbSeries = await _seriesDataService.GetByIdAsync(series.SeriesId, ct);

            if (dbSeries is not null)
            {
                dbSeries.AppleMusicArtistId = bestMatch.ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await _seriesDataService.UpdateAsync(dbSeries, ct);
                _logger.Debug(() => $"iTunes-Artist-ID {bestMatch.ArtistId} für '{series.Title}' gespeichert.");
            }

            return bestMatch.ArtistId;
        }

        /// <summary>
        /// Delegiert die Folgennummer-Extraktion an den zentralen Parser in Core.
        /// </summary>
        /// <param name="albumName">Der Albumname, aus dem die Folgennummer gelesen wird.</param>
        internal static int? ExtractEpisodeNumber(string albumName)
        {
            return EchoPlay.Core.Parsing.EpisodeNumberParser.Extract(albumName);
        }

        /// <summary>
        /// Ermittelt die höchste Episodennummer aus den lokalen Ordnernamen, mit derselben Regel
        /// wie die Fehlende-Folgen-Analyse: jeder Ordner gegen alle Muster, nur Ordner mit Audio.
        /// </summary>
        /// <param name="localFolderPath">Pfad zum Serienordner, oder null wenn nicht lokal vorhanden.</param>
        /// <param name="logger">Protokollkanal für einen nicht lesbaren Serienordner; ohne ihn bleibt es bei der 0.</param>
        /// <returns>Die höchste gefundene Nummer, oder 0 wenn kein Ordner vorhanden oder keine Nummern erkannt.</returns>
        internal static int GetLocalHighestEpisodeNumber(string? localFolderPath, EchoPlay.Logger.Abstractions.ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(localFolderPath) || !Directory.Exists(localFolderPath))
            {
                return 0;
            }

            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(localFolderPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Mit 0 gilt jede Folge beim Anbieter als neu; der Grund steht im Protokoll.
                logger?.Warning("Serienordner nicht lesbar, höchste lokale Folge unbekannt: {Folder} – {Reason}", EchoPlay.Core.Logging.PathRedactor.Redact(localFolderPath), ex.Message);
                return 0;
            }

            // Ordnernamen extrahieren (nur der Name, kein Pfad)
            // Ein Ordner ohne Audio ist keine vorhandene Folge; zählte er mit, schiene die Serie
            // weiter, als sie ist, und neue Folgen beim Anbieter fielen unter den Tisch.
            List<string> folderNames = [];
            foreach (string folder in subfolders.Where(EchoPlay.LocalLibrary.Scanning.EpisodeAudioFiles.HasAudio))
            {
                string? name = Path.GetFileName(folder);
                if (name is not null)
                {
                    folderNames.Add(name);
                }
            }

            // Gemeinsame Erkennung mit der Fehlende-Folgen-Analyse: jeder Ordner wird gegen alle
            // Muster geprüft, Jahreszahlen fallen raus. Ein einziges Gewinner-Muster
            // ließe abweichend benannte Ordner die höchste Nummer verfälschen.
            return LocalEpisodeNumbers.Highest(folderNames);
        }
    }
}
