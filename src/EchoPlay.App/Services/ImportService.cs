using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Koordiniert die Suche und den Import von Hörspielserien aus externen Quellen.
    /// Wählt anhand der aktiven AppSettings den richtigen Provider aus und
    /// persistiert neue Serien und Episoden in der Datenbank.
    /// Alle Abhängigkeiten werden über einen eigenen DI-Scope aufgelöst,
    /// damit dieser Service Singleton-kompatibel bleibt.
    ///
    /// Provider-Auswahl: Keyed-Services mit "Spotify" bzw. "AppleMusic" als Schlüssel.
    /// </summary>

    public sealed class ImportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EpisodeCoverCacheService _coverCacheService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den ImportService.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes.</param>
        /// <param name="coverCacheService">Service zum Herunterladen und Cachen von Episoden-Covern.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>

        public ImportService(
            IServiceScopeFactory scopeFactory,
            EpisodeCoverCacheService coverCacheService,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _coverCacheService = coverCacheService;
            _logger = loggerFactory.CreateLogger("ImportService");
        }

        /// <summary>
        /// Sucht nach Hörspielserien beim aktiven Provider laut AppSettings.
        /// Gibt ein <see cref="SearchOutcome"/> mit leerer Trefferliste zurück, wenn keine Ergebnisse gefunden werden
        /// oder kein Provider aktiv ist. Fallback auf Apple Music, falls Spotify als aktiver Provider konfiguriert
        /// ist, aber keine Credentials hinterlegt wurden.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <returns>Trefferliste plus Flag, ob der Spotify-Fallback gegriffen hat.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <exception cref="ArgumentException">Wird geworfen, wenn <paramref name="query"/> leer oder nur Leerzeichen enthält.</exception>
        public async Task<SearchOutcome> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            using IServiceScope scope = _scopeFactory.CreateScope();

            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);

            // Ohne aktiven Provider kann keine Online-Suche stattfinden.
            if (settings.ActiveProvider == ProviderType.None)
            {
                return new SearchOutcome([], SpotifyFallbackApplied: false);
            }

            (ProviderType importProvider, bool spotifyFallbackApplied) =
                await ResolveProviderAsync(scope, settings.ActiveProvider, cancellationToken);

            // Provider-Schlüssel entspricht dem Enum-Namen ("Spotify" / "AppleMusic")
            string providerKey = importProvider.ToString();
            _logger.Debug(() => $"Suche nach \"{query}\" via {providerKey}");
            ISeriesImportSearch search = scope.ServiceProvider.GetRequiredKeyedService<ISeriesImportSearch>(providerKey);

            IReadOnlyList<ImportSeries> results = await search.SearchAsync(query, cancellationToken);
            return new SearchOutcome(results, spotifyFallbackApplied);
        }

        /// <summary>
        /// Wählt den effektiven Provider für einen Suchlauf. Bildet das bestehende Both→AppleMusic-Mapping
        /// ab und prüft zusätzlich, ob Spotify-Credentials hinterlegt sind. Fehlen sie, wird transparent
        /// auf Apple Music umgelenkt und ein Warning geloggt — die AppSettings bleiben unverändert.
        /// </summary>
        /// <param name="scope">DI-Scope für den Credential-Store-Lookup.</param>
        /// <param name="activeProvider">Vom Nutzer gewählter Provider aus den AppSettings.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Tuple aus effektivem Provider und Hinweis-Flag, ob ein Spotify→Apple-Music-Fallback gegriffen hat.</returns>
        private async Task<(ProviderType ResolvedProvider, bool SpotifyFallbackApplied)> ResolveProviderAsync(
            IServiceScope scope,
            ProviderType activeProvider,
            CancellationToken cancellationToken)
        {
            ProviderType importProvider = activeProvider == ProviderType.Both
                ? ProviderType.AppleMusic
                : activeProvider;

            if (importProvider != ProviderType.Spotify)
            {
                return (importProvider, SpotifyFallbackApplied: false);
            }

            ISpotifyClientCredentialsProvider credentialsProvider =
                scope.ServiceProvider.GetRequiredService<ISpotifyClientCredentialsProvider>();
            SpotifyClientCredentials? credentials = await credentialsProvider.GetAsync(cancellationToken);

            if (credentials is not null)
            {
                return (ProviderType.Spotify, SpotifyFallbackApplied: false);
            }

            _logger.Warning("Spotify-Credentials fehlen — Suche fällt auf Apple Music zurück.");
            return (ProviderType.AppleMusic, SpotifyFallbackApplied: true);
        }

        /// <summary>
        /// Sucht beim aktiven Provider nach Alben (einzelnen Folgen) anhand eines Suchbegriffs.
        /// Ergänzt die Seriensuche (<see cref="SearchAsync"/>) um die Möglichkeit,
        /// gezielt nach Folgentiteln zu suchen (z.B. "Kapatenhund"). Teilt das Spotify→Apple-Music-Fallback-
        /// Verhalten von <see cref="SearchAsync"/>.
        /// </summary>
        /// <param name="query">Suchbegriff – wird an die Provider-API weitergereicht.</param>
        /// <returns>Album-Treffer plus Flag, ob der Spotify-Fallback gegriffen hat.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<SearchOutcome> SearchAlbumsAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new SearchOutcome([], SpotifyFallbackApplied: false);
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);

            if (settings.ActiveProvider == ProviderType.None)
            {
                return new SearchOutcome([], SpotifyFallbackApplied: false);
            }

            (ProviderType importProvider, bool spotifyFallbackApplied) =
                await ResolveProviderAsync(scope, settings.ActiveProvider, cancellationToken);

            _logger.Debug(() => $"Album-Suche nach \"{query}\" via {importProvider}");

            List<ImportSeries> results = [];

            if (importProvider == ProviderType.Spotify)
            {
                EchoPlay.Spotify.Abstractions.ISpotifyApiClient? spotifyClient =
                    scope.ServiceProvider.GetService<EchoPlay.Spotify.Abstractions.ISpotifyApiClient>();

                if (spotifyClient is null)
                {
                    return new SearchOutcome([], spotifyFallbackApplied);
                }

                IReadOnlyList<EchoPlay.Spotify.Dtos.SpotifyAlbumDto> albums =
                    await spotifyClient.SearchAlbumsAsync(query, 15, cancellationToken);

                foreach (EchoPlay.Spotify.Dtos.SpotifyAlbumDto album in albums)
                {
                    results.Add(new ImportSeries
                    {
                        SourceSeriesId = album.SpotifyAlbumId,
                        Source = ProviderKeys.Spotify,
                        Title = album.Title,
                        ArtistName = album.ArtistName,
                        CoverImageUrl = album.ImageUrl,
                        IsAlbumResult = true,
                        IsHoerspiel = true,
                        Score = 50
                    });
                }
            }
            else if (importProvider == ProviderType.AppleMusic)
            {
                EchoPlay.AppleMusic.Abstractions.IAppleMusicSearchClient? appleClient =
                    scope.ServiceProvider.GetService<EchoPlay.AppleMusic.Abstractions.IAppleMusicSearchClient>();

                if (appleClient is null)
                {
                    return new SearchOutcome([], spotifyFallbackApplied);
                }

                EchoPlay.AppleMusic.Dtos.ITunesResponseDto<EchoPlay.AppleMusic.Dtos.ITunesCollectionDto> response =
                    await appleClient.SearchAlbumsAsync(query, 15, cancellationToken);

                foreach (EchoPlay.AppleMusic.Dtos.ITunesCollectionDto album in response.Results)
                {
                    if (!string.Equals(album.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(new ImportSeries
                    {
                        SourceSeriesId = album.CollectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Source = ProviderKeys.AppleMusic,
                        Title = album.CollectionName,
                        ArtistName = album.ArtistName,
                        CoverImageUrl = null,
                        IsAlbumResult = true,
                        IsHoerspiel = true,
                        Score = 50
                    });
                }
            }

            return new SearchOutcome(results, spotifyFallbackApplied);
        }

        /// <summary>
        /// Prüft, ob eine Serie bereits in der Datenbank vorhanden ist (via SourceSeriesId).
        /// </summary>
        /// <param name="series">Die zu prüfende ImportSerie.</param>
        /// <returns>True wenn die Serie bereits importiert wurde.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<bool> IsAlreadyImportedAsync(ImportSeries series, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(series);
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            return await FindExistingSeriesAsync(seriesService, series, cancellationToken) is not null;
        }

        /// <summary>
        /// Importiert eine Serie vollständig: legt Series und alle Episoden in der DB an.
        /// Existiert die Serie bereits (gleiche SourceSeriesId), wird sie übersprungen.
        /// Fortschrittsmeldungen werden über <paramref name="progress"/> gemeldet, falls angegeben.
        /// </summary>
        /// <param name="importSeries">Die zu importierende Serie.</param>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback für UI-Updates während des Imports.
        /// Meldet Texte wie "Lade Episoden …" und "Speichere Episoden … (10/250)".
        /// <see langword="null"/> wenn kein Fortschritt gemeldet werden soll.
        /// </param>
        /// <returns>Die ID der neuen oder bereits vorhandenen Serie.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<Guid> ImportAsync(ImportSeries importSeries, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(importSeries);
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.Import);
            using IServiceScope scope = _scopeFactory.CreateScope();

            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            // Früh abbrechen, falls bereits importiert
            Series? existing = await FindExistingSeriesAsync(seriesService, importSeries, cancellationToken);

            if (existing is not null)
            {
                _logger.Debug(() => $"Import übersprungen – bereits vorhanden: \"{importSeries.Title}\" ({importSeries.Source})");
                return existing.Id;
            }

            _logger.Info("Import gestartet: \"{Title}\" ({Source})", importSeries.Title, importSeries.Source);

            // Serie anlegen und persistieren – Id wird von EF nach SaveChanges gesetzt
            Series series = MapToSeries(importSeries);
            await seriesService.AddAsync(series, cancellationToken);

            // Episoden laden – bei großen Serien (>100 Episoden) kann dieser HTTP-Aufruf mehrere Sekunden dauern
            progress?.Report($"Lade Episoden für \"{importSeries.Title}\" \u2026");
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(importSeries.Source);
            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(importSeries.SourceSeriesId, cancellationToken: cancellationToken);

            // Schutzgitter: doppelte SourceEpisodeIds (Provider-Duplikate, Re-Releases,
            // Compilation-Alben mit identischer CollectionId) werden hier idempotent verworfen,
            // bevor der Insert l\u00e4uft. Ohne diese Stufe entst\u00fcnden bei mehrfacher Auslieferung
            // derselben Folge mehrere Episode-Zeilen mit identischer Provider-ID.
            List<ImportEpisode> uniqueEpisodes = DeduplicateBySourceEpisodeId(episodes, importSeries.Title);

            progress?.Report($"Speichere Episoden \u2026 ({uniqueEpisodes.Count})");

            // Batch-Insert: ein einziger SaveChangesAsync-Aufruf statt N. Bei einer
            // Hörspielserie mit 200 Folgen ersetzt das 200 DB-Roundtrips durch einen.
            List<Episode> mappedEpisodes = new(uniqueEpisodes.Count);
            for (int i = 0; i < uniqueEpisodes.Count; i++)
            {
                mappedEpisodes.Add(MapToEpisode(uniqueEpisodes[i], series.Id));
            }

            await episodeService.AddRangeAsync(mappedEpisodes, cancellationToken);

            _logger.Info("Import abgeschlossen: \"{Title}\", {EpisodeCount} Episoden", importSeries.Title, uniqueEpisodes.Count);

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
            _ = _coverCacheService.CacheCoversAsync(series.Id, uniqueEpisodes, ct: cancellationToken);

            return series.Id;
        }

        /// <summary>
        /// Importiert Episoden für eine bestehende Online-Serie neu vom Provider.
        /// Wird nach einer Migration aufgerufen, die alte Episoden bereinigt hat –
        /// der Nutzer muss nicht manuell neu importieren.
        /// Existierende Episoden der Serie werden nicht gelöscht; nur fehlende werden nachgeladen.
        /// </summary>
        /// <param name="series">Die bestehende Serie mit gesetzter SpotifyArtistId oder AppleMusicArtistId.</param>
        /// <returns>Anzahl der neu angelegten Episoden. 0 wenn kein Provider zugeordnet oder keine Episoden gefunden.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<int> ReImportEpisodesAsync(Series series, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(series);

            (string providerKey, string sourceSeriesId)? resolved = ResolveProviderForSeries(series);
            if (resolved is null)
            {
                _logger.Debug(() => $"Kein Provider für Serie \"{series.Title}\" – Re-Import übersprungen");
                return 0;
            }
            (string providerKey, string sourceSeriesId) = resolved.Value;

            _logger.Info("Re-Import gestartet: \"{Title}\" via {ProviderKey}", series.Title, providerKey);

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(sourceSeriesId, cancellationToken: cancellationToken);

            // Batch-Insert: ein einziger SaveChangesAsync-Aufruf statt N (analog ImportAsync).
            // Bei einer Serie mit 200 Folgen ersetzt das 200 DB-Roundtrips durch einen.
            List<Episode> mappedEpisodes = new(episodes.Count);
            foreach (ImportEpisode importEpisode in episodes)
            {
                mappedEpisodes.Add(MapToEpisode(importEpisode, series.Id));
            }

            await episodeService.AddRangeAsync(mappedEpisodes, cancellationToken);
            int count = mappedEpisodes.Count;

            _logger.Info("Re-Import abgeschlossen: \"{Title}\", {EpisodeCount} Episoden nachgeladen", series.Title, count);

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
            if (count > 0)
            {
                _ = _coverCacheService.CacheCoversAsync(series.Id, episodes, ct: cancellationToken);
            }

            return count;
        }

        /// <summary>
        /// Prüft eine Online-Serie auf neue Folgen beim Provider und importiert nur die Differenz.
        /// Bereits vorhandene Episoden (Titelvergleich) werden übersprungen.
        /// Geeignet für regelmäßige Aktualisierung – deutlich schneller als ein Voll-Import.
        /// </summary>
        /// <param name="series">Die bestehende Serie mit gesetzter Provider-ID.</param>
        /// <returns>Anzahl der neu importierten Episoden. 0 wenn keine neuen gefunden.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<int> DeltaImportEpisodesAsync(Series series, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(series);

            (string providerKey, string sourceSeriesId)? resolved = ResolveProviderForSeries(series);
            if (resolved is null) return 0;
            (string providerKey, string sourceSeriesId) = resolved.Value;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            // Bestehende Episoden in ein Title-Lookup ziehen – ein einmaliger DB-Roundtrip,
            // statt der Schleife pro Treffer ein neues FirstOrDefault auf die Liste loszuwerfen.
            IReadOnlyList<Episode> existingEpisodes = await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken);
            Dictionary<string, Episode> existingByTitle = new(existingEpisodes.Count, StringComparer.OrdinalIgnoreCase);

            foreach (Episode existing in existingEpisodes)
            {
                // Doppelte Titel: ersten Treffer behalten – das spätere Update bevorzugt damit deterministisch
                // die Episode mit der niedrigsten EpisodeNumber bzw. dem ältesten Datensatz.
                _ = existingByTitle.TryAdd(existing.Title, existing);
            }

            // Delta: bekannte Titel als Hinweis mitgeben. Die Quelle spart dadurch den teuren
            // Track-Lookup für bestehende Folgen (nur die Dauer bräuchte ihn), liefert deren
            // Metadaten inkl. Cover aber weiterhin – so kostet der Abgleich nur so viele Track-
            // Lookups wie es neue Folgen gibt, und fehlende Cover lassen sich trotzdem nachtragen.
            IReadOnlyList<ImportEpisode> providerEpisodes = await episodeSource.GetEpisodesAsync(
                sourceSeriesId,
                new HashSet<string>(existingByTitle.Keys, StringComparer.OrdinalIgnoreCase),
                cancellationToken);

            // Add- und Update-Pfad getrennt sammeln; jeder Pfad löst genau einen DB-Roundtrip aus.
            List<Episode> newEpisodes = [];
            List<Episode> updatedEpisodes = [];

            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                // Titel-basierter Vergleich – robuster als Nummernvergleich,
                // da Online-Episoden nicht immer eine konsistente Folgennummer haben
                if (existingByTitle.TryGetValue(importEpisode.Title, out Episode? existing))
                {
                    // Bestehende Episode: CoverImageUrl nachtragen falls noch nicht gesetzt
                    if (!string.IsNullOrEmpty(importEpisode.CoverImageUrl)
                        && string.IsNullOrEmpty(existing.CoverImageUrl))
                    {
                        existing.CoverImageUrl = importEpisode.CoverImageUrl;
                        updatedEpisodes.Add(existing);
                    }

                    continue;
                }

                newEpisodes.Add(MapToEpisode(importEpisode, series.Id));
            }

            if (newEpisodes.Count > 0)
            {
                await episodeService.AddRangeAsync(newEpisodes, cancellationToken);
            }

            if (updatedEpisodes.Count > 0)
            {
                await episodeService.UpdateRangeAsync(updatedEpisodes, cancellationToken);
            }

            int newCount = newEpisodes.Count;

            if (newCount > 0)
            {
                _logger.Info("Delta-Import: {NewCount} neue Episoden für \"{Title}\"", newCount, series.Title);

                // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
                _ = _coverCacheService.CacheCoversAsync(series.Id, providerEpisodes, ct: cancellationToken);
            }

            return newCount;
        }

        /// <summary>
        /// Filtert Episoden mit doppelter <see cref="ImportEpisode.SourceEpisodeId"/> heraus
        /// und behält nur das erste Vorkommen. Nötig, weil iTunes-Lookups bei Compilation-/
        /// Various-Artists-Alben dasselbe Album mehrfach liefern können – ohne diese Stufe
        /// entstünden mehrere Episode-Zeilen mit identischer Provider-ID.
        /// Loggt eine Warnung, wenn Duplikate verworfen wurden, damit Provider-Anomalien
        /// im Triage-Log sichtbar bleiben.
        /// </summary>

        private List<ImportEpisode> DeduplicateBySourceEpisodeId(
            IReadOnlyList<ImportEpisode> episodes,
            string seriesTitle)
        {
            HashSet<string> seenIds = new(StringComparer.Ordinal);
            List<ImportEpisode> unique = new(episodes.Count);
            int skipped = 0;

            foreach (ImportEpisode episode in episodes)
            {
                if (seenIds.Add(episode.SourceEpisodeId))
                {
                    unique.Add(episode);
                    continue;
                }

                skipped++;
            }

            if (skipped > 0)
            {
                _logger.Warning("Import: {SkippedCount} doppelte Episoden-Treffer für \"{SeriesTitle}\" verworfen.", skipped, seriesTitle);
            }

            return unique;
        }

        /// <summary>
        /// Sucht eine bestehende Serie anhand der externen ID und Quelle.
        /// </summary>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="service">Parameter <c>service</c>.</param>
        /// <param name="series">Parameter <c>series</c>.</param>
        private static async Task<Series?> FindExistingSeriesAsync(ISeriesDataService service, ImportSeries series, CancellationToken cancellationToken = default)
        {
            return series.Source switch
            {
                ProviderKeys.Spotify => await service.GetBySpotifyArtistIdAsync(series.SourceSeriesId, cancellationToken),
                ProviderKeys.AppleMusic => await service.GetByAppleMusicArtistIdAsync(series.SourceSeriesId, cancellationToken),
                _ => null
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Series"/>-Entität aus einem <see cref="ImportSeries"/>-Modell.
        /// Setzt die provider-spezifische Artist-ID anhand der Source-Bezeichnung.
        /// Import und Abonnement sind dasselbe Konzept – jede importierte Serie ist direkt abonniert
        /// und erscheint sofort im Dashboard und in der Mediathek.
        /// </summary>

        /// <param name="importSeries">Parameter <c>importSeries</c>.</param>
        private static Series MapToSeries(ImportSeries importSeries)
        {
            return new Series
            {
                Title = importSeries.Title,
                Description = importSeries.Description,
                CoverImageUrl = importSeries.CoverImageUrl,
                SpotifyArtistId = importSeries.Source == ProviderKeys.Spotify ? importSeries.SourceSeriesId : null,
                AppleMusicArtistId = importSeries.Source == ProviderKeys.AppleMusic ? importSeries.SourceSeriesId : null,
                IsOnlineImported = true,
                IsSubscribed = true
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Episode"/>-Entität aus einem <see cref="ImportEpisode"/>-Modell.
        /// Setzt die provider-spezifische Album-ID anhand der Source-Bezeichnung.
        /// </summary>


        /// <param name="importEpisode">Parameter <c>importEpisode</c>.</param>
        /// <param name="seriesId">Parameter <c>seriesId</c>.</param>
        private static Episode MapToEpisode(ImportEpisode importEpisode, Guid seriesId)
        {
            return new Episode
            {
                SeriesId = seriesId,
                Title = importEpisode.Title,
                EpisodeNumber = importEpisode.EpisodeNumber,
                ReleaseDate = importEpisode.ReleaseDate,
                Duration = importEpisode.Duration,
                ProviderUrl = importEpisode.ProviderUrl,
                CoverImageUrl = importEpisode.CoverImageUrl,
                SpotifyAlbumId = importEpisode.Source == ProviderKeys.Spotify ? importEpisode.SourceEpisodeId : null,
                AppleMusicAlbumId = importEpisode.Source == ProviderKeys.AppleMusic ? importEpisode.SourceEpisodeId : null,
            };
        }

        // Spotify hat Vorrang vor Apple Music, weil Spotify-IDs reicher sind (Album-IDs).
        private static (string ProviderKey, string SourceSeriesId)? ResolveProviderForSeries(Series series)
        {
            if (series.SpotifyArtistId is not null)
            {
                return (ProviderKeys.Spotify, series.SpotifyArtistId);
            }
            if (series.AppleMusicArtistId is not null)
            {
                return (ProviderKeys.AppleMusic, series.AppleMusicArtistId);
            }
            return null;
        }
    }
}
