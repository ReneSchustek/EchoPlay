using EchoPlay.Core.Abstractions.Time;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Parsing;
using System.Text.RegularExpressions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Prüft abonnierte Serien gegen die iTunes API auf neue Folgen.
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
        /// <summary>
        /// Pause zwischen iTunes-API-Aufrufen – iTunes erlaubt ~20 Requests/Minute.
        /// 1,5 Sekunden sind ein guter Kompromiss: schnell genug für viele Serien,
        /// aber unter dem Limit von ~20 Anfragen pro Minute.
        /// </summary>
        private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// Regex zum Extrahieren der ersten Zahl aus einem iTunes-Albumnamen.
        /// Hörspielserien haben typisch Alben wie "Die drei ??? - Folge 229 - Titel"
        /// oder "TKKG - 218 - Titel". Die erste Zahl im Titel ist fast immer die Folgennummer.
        /// </summary>

        /// <summary>
        /// Parser-Kandidaten für lokale Ordnernamen – gleiche Muster wie in der
        /// Fehlende-Folgen-Analyse (<see cref="EpisodeFolderParser"/>).
        /// </summary>
        private static readonly EpisodeFolderParser[] FolderParsers =
        [
            new("{number:000} - {title}"),
            new("{*} - {number:000} - {title}"),
            new("Folge {number:000} - {title}"),
            new("{number:000}_{title}"),
            new("{number} - {title}"),
            new("{title} - {number:000}"),
            new("{number:000} {title}"),
            new("{*} - {number} - {title}")
        ];

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
        public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            DateTime cutoffDate,
            CancellationToken cancellationToken = default)
        {
            return CheckSeriesSequentiallyAsync(
                subscribedSeries,
                (series, ct) => CheckNewReleasesForSeriesAsync(series, cutoffDate, ct),
                cancellationToken);
        }

        /// <summary>
        /// Gemeinsame Schleife für sequenzielle Serienprüfungen mit Rate-Limiting.
        /// Jede Serie wird einzeln geprüft – Fehler bei einer Serie blockieren die restlichen nicht.
        /// </summary>
        /// <param name="subscribedSeries">Zu prüfende Serien.</param>
        /// <param name="checkFunc">Prüflogik pro Serie (CheckSingle oder CheckNewReleases).</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Ergebnisse aller erfolgreich geprüften Serien.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sequenzieller Mehr-Serien-Check: HTTP-/Provider-Fehler einer einzelnen Serie werden geloggt und übersprungen, damit die restlichen Serien weiterhin geprueft werden. Der Aufrufer erhaelt die Liste der erfolgreichen Ergebnisse.")]
        private async Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckSeriesSequentiallyAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            Func<CheckableSeriesInfo, CancellationToken, Task<OnlineEpisodeCheckResult?>> checkFunc,
            CancellationToken cancellationToken)
        {
            List<OnlineEpisodeCheckResult> results = [];

            for (int i = 0; i < subscribedSeries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CheckableSeriesInfo series = subscribedSeries[i];

                try
                {
                    OnlineEpisodeCheckResult? result = await checkFunc(series, cancellationToken);

                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Prüfung für '{series.Title}' fehlgeschlagen: {ex.Message}");
                }

                // Rate-Limiting: Pause vor dem nächsten API-Aufruf (nicht nach dem letzten)
                if (i < subscribedSeries.Count - 1)
                {
                    await Task.Delay(RateLimitDelay, cancellationToken);
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
            // Apple Music Artist ID ermitteln
            long? artistId = await ResolveArtistIdAsync(series, ct);

            if (artistId is null)
            {
                _logger.Debug($"Keine iTunes-Artist-ID für '{series.Title}' gefunden – übersprungen.");
                return null;
            }

            // Alle Alben des Künstlers laden
            ITunesResponseDto<ITunesCollectionDto> albumResponse = await _appleMusicClient
                .LookupAlbumsAsync(artistId.Value, ct);

            List<ITunesCollectionDto> albums = albumResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (albums.Count == 0)
            {
                return null;
            }

            // Alben im Zeitfenster sammeln (Cutoff bis heute) und Ankündigungen (Zukunft)
            DateTime today = _clock.UtcNow.Date;
            List<NewReleaseEpisode> newReleases = [];
            List<AnnouncedEpisode> announced = [];

            foreach (ITunesCollectionDto album in albums)
            {
                if (!TryParseReleaseDate(album.ReleaseDate, out DateTime releaseDate))
                {
                    continue;
                }

                int? episodeNumber = ExtractEpisodeNumber(album.CollectionName);

                if (releaseDate.Date > today)
                {
                    // Angekündigt: Datum in der Zukunft
                    announced.Add(new AnnouncedEpisode
                    {
                        Title = album.CollectionName,
                        ReleaseDate = releaseDate
                    });

                    // Ankündigungen auch als NewRelease aufnehmen (mit Zukunftsdatum)
                    newReleases.Add(new NewReleaseEpisode
                    {
                        Title = album.CollectionName,
                        EpisodeNumber = episodeNumber,
                        ReleaseDate = releaseDate,
                        CoverUrl = album.ArtworkUrl100,
                        CollectionId = album.CollectionId
                    });
                }
                else if (releaseDate.Date >= cutoffDate.Date)
                {
                    // Neuerscheinung: im Zeitfenster
                    newReleases.Add(new NewReleaseEpisode
                    {
                        Title = album.CollectionName,
                        EpisodeNumber = episodeNumber,
                        ReleaseDate = releaseDate,
                        CoverUrl = album.ArtworkUrl100,
                        CollectionId = album.CollectionId
                    });
                }
            }

            // Keine relevanten Ergebnisse? Serie nicht ins Ergebnis aufnehmen.
            if (newReleases.Count == 0 && announced.Count == 0)
            {
                return null;
            }

            // Neueste zuerst sortieren
            newReleases.Sort((a, b) => b.ReleaseDate.CompareTo(a.ReleaseDate));

            // Lokalen Stand ermitteln (für Höchstnummer-Vergleich)
            int localHighest = GetLocalHighestEpisodeNumber(series.LocalFolderPath);
            int onlineHighest = newReleases
                .Where(e => e.EpisodeNumber.HasValue)
                .Select(e => e.EpisodeNumber!.Value)
                .DefaultIfEmpty(0)
                .Max();

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

        /// <summary>
        /// Prüft eine einzelne Serie gegen die iTunes API.
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="ct">Abbruchtoken für die API-Aufrufe.</param>
        /// <returns>Das Prüfergebnis, oder null wenn keine Artist ID ermittelt werden konnte.</returns>
        private async Task<OnlineEpisodeCheckResult?> CheckSingleSeriesAsync(
            CheckableSeriesInfo series, CancellationToken ct = default)
        {
            // Schritt 1: Apple Music Artist ID ermitteln
            long? artistId = await ResolveArtistIdAsync(series, ct);

            if (artistId is null)
            {
                _logger.Debug($"Keine iTunes-Artist-ID für '{series.Title}' gefunden – übersprungen.");
                return null;
            }

            // Schritt 2: Alle Alben des Künstlers laden
            ITunesResponseDto<ITunesCollectionDto> albumResponse = await _appleMusicClient
                .LookupAlbumsAsync(artistId.Value, ct);

            List<ITunesCollectionDto> albums = albumResponse.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (albums.Count == 0)
            {
                return null;
            }

            // Schritt 3: Episodennummern aus Albumnamen extrahieren
            int onlineHighest = 0;
            List<AnnouncedEpisode> announced = [];
            // Nummer → Albumname für die spätere Fehlende-Folgen-Liste
            Dictionary<int, string> episodesByNumber = [];
            DateTime today = _clock.UtcNow.Date;

            foreach (ITunesCollectionDto album in albums)
            {
                int? episodeNumber = ExtractEpisodeNumber(album.CollectionName);

                // ExtractEpisodeNumber filtert bereits auf 1–999
                if (episodeNumber.HasValue)
                {
                    // Prüfen ob die Folge angekündigt ist (Erscheinungsdatum in der Zukunft)
                    if (TryParseReleaseDate(album.ReleaseDate, out DateTime releaseDate) && releaseDate > today)
                    {
                        announced.Add(new AnnouncedEpisode
                        {
                            Title = album.CollectionName,
                            ReleaseDate = releaseDate
                        });
                    }

                    // Albumname merken – wird für die Fehlende-Folgen-Liste benötigt
                    _ = episodesByNumber.TryAdd(episodeNumber.Value, album.CollectionName);

                    if (episodeNumber.Value > onlineHighest)
                    {
                        onlineHighest = episodeNumber.Value;
                    }
                }
            }

            // Schritt 4: Lokale Ordner scannen
            int localHighest = GetLocalHighestEpisodeNumber(series.LocalFolderPath);

            // Schritt 5: Fehlende Folgen (online vorhanden, lokal nicht) sammeln
            List<MissingOnlineEpisode> missingOnline = [];
            for (int n = localHighest + 1; n <= onlineHighest; n++)
            {
                string albumTitle = episodesByNumber.TryGetValue(n, out string? name)
                    ? name
                    : FormattableString.Invariant($"Folge {n:D3}");

                missingOnline.Add(new MissingOnlineEpisode
                {
                    EpisodeNumber = n,
                    AlbumTitle = albumTitle
                });
            }

            int newCount = Math.Max(0, onlineHighest - localHighest);

            return new OnlineEpisodeCheckResult
            {
                SeriesId = series.SeriesId,
                SeriesTitle = series.Title,
                OnlineHighestNumber = onlineHighest,
                LocalHighestNumber = localHighest,
                NewEpisodesCount = newCount,
                AnnouncedEpisodes = announced,
                MissingOnlineEpisodes = missingOnline,
                CoverUrl = series.CoverImageUrl,
                CheckedAtUtc = _clock.UtcNow
            };
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
            _logger.Debug($"Suche iTunes-Artist-ID für '{series.Title}' per Namenssuche...");

            ITunesResponseDto<ITunesArtistDto> searchResponse = await _appleMusicClient
                .SearchArtistsAsync(series.Title, limit: 5, ct);

            // Den besten Treffer nehmen – idealerweise exakte Übereinstimmung im Namen
            ITunesArtistDto? bestMatch = searchResponse.Results
                .FirstOrDefault(a => string.Equals(a.ArtistName, series.Title, StringComparison.OrdinalIgnoreCase))
                ?? searchResponse.Results.FirstOrDefault();

            if (bestMatch is null)
            {
                return null;
            }

            // Gefundene ID in der Datenbank persistieren, damit beim nächsten Check kein Suchaufruf nötig ist
            Data.Entities.Library.Series? dbSeries = await _seriesDataService.GetByIdAsync(series.SeriesId);

            if (dbSeries is not null)
            {
                dbSeries.AppleMusicArtistId = bestMatch.ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await _seriesDataService.UpdateAsync(dbSeries);
                _logger.Debug($"iTunes-Artist-ID {bestMatch.ArtistId} für '{series.Title}' gespeichert.");
            }

            return bestMatch.ArtistId;
        }

        /// <summary>
        /// Delegiert die Folgennummer-Extraktion an den zentralen Parser in Core.
        /// </summary>
        internal static int? ExtractEpisodeNumber(string albumName)
        {
            return EchoPlay.Core.Parsing.EpisodeNumberParser.Extract(albumName);
        }

        /// <summary>
        /// Ermittelt die höchste Episodennummer aus den lokalen Ordnernamen.
        /// Verwendet dieselbe Parser-Logik wie die Fehlende-Folgen-Analyse:
        /// Alle Kandidatenmuster werden getestet, das Muster mit den meisten Treffern gewinnt.
        /// </summary>
        /// <param name="localFolderPath">Pfad zum Serienordner, oder null wenn nicht lokal vorhanden.</param>
        /// <returns>Die höchste gefundene Nummer, oder 0 wenn kein Ordner vorhanden oder keine Nummern erkannt.</returns>
        internal static int GetLocalHighestEpisodeNumber(string? localFolderPath)
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
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }

            // Ordnernamen extrahieren (nur der Name, kein Pfad)
            List<string> folderNames = [];
            foreach (string folder in subfolders)
            {
                string? name = Path.GetFileName(folder);
                if (name is not null)
                {
                    folderNames.Add(name);
                }
            }

            if (folderNames.Count == 0)
            {
                return 0;
            }

            // Bestes Muster ermitteln (das mit den meisten Treffern für Nummern > 0)
            EpisodeFolderParser? bestParser = null;
            int bestMatchCount = 0;

            foreach (EpisodeFolderParser parser in FolderParsers)
            {
                int matchCount = folderNames
                    .Count(name => parser.TryParse(name, out int? num, out _) && num is > 0);

                if (matchCount > bestMatchCount)
                {
                    bestMatchCount = matchCount;
                    bestParser = parser;
                }
            }

            if (bestParser is null)
            {
                return 0;
            }

            // Höchste Nummer mit dem besten Parser extrahieren
            int maxNumber = 0;

            foreach (string name in folderNames)
            {
                if (bestParser.TryParse(name, out int? number, out _) && number is > 0)
                {
                    if (number.Value > maxNumber)
                    {
                        maxNumber = number.Value;
                    }
                }
            }

            return maxNumber;
        }

        /// <summary>
        /// Versucht ein iTunes-Releasedatum zu parsen.
        /// iTunes liefert Daten im ISO-8601-Format (z.B. "2026-04-15T07:00:00Z").
        /// </summary>
        private static bool TryParseReleaseDate(string? dateString, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(dateString))
            {
                return false;
            }

            return DateTime.TryParse(dateString, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out result);
        }
    }
}
