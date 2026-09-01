using EchoPlay.App.Infrastructure;
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

        // Die Suche beim Anbieter — sie schreibt nichts und gehört deshalb nicht
        // in denselben Typ wie der Import.
        private readonly ProviderSearch _providerSearch;

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
            _providerSearch = new ProviderSearch(scopeFactory, _logger);
        }

        /// <summary>
        /// Sucht Serien beim Anbieter.
        /// </summary>
        /// <param name="query">Der Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchzeichen der umgebenden Operation.</param>
        /// <returns>Die Treffer und der Hinweis, ob auf Apple Music ausgewichen wurde.</returns>
        public Task<SearchOutcome> SearchAsync(string query, CancellationToken cancellationToken = default)
            => _providerSearch.SearchAsync(query, cancellationToken);

        /// <summary>
        /// Sucht Serien beim Anbieter und meldet jeden Treffer, sobald er feststeht.
        /// </summary>
        /// <param name="query">Der Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchzeichen der umgebenden Operation.</param>
        /// <returns>Die Treffer in der Reihenfolge, in der der Anbieter sie bewertet.</returns>
        public IAsyncEnumerable<ImportSeries> SearchStreamAsync(string query, CancellationToken cancellationToken = default)
            => _providerSearch.SearchStreamAsync(query, cancellationToken);

        /// <summary>
        /// Sucht Alben beim Anbieter — einzelne Veröffentlichungen statt ganzer Serien.
        /// </summary>
        /// <param name="query">Der Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchzeichen der umgebenden Operation.</param>
        /// <returns>Die Treffer und der Hinweis, ob auf Apple Music ausgewichen wurde.</returns>
        public Task<SearchOutcome> SearchAlbumsAsync(string query, CancellationToken cancellationToken = default)
            => _providerSearch.SearchAlbumsAsync(query, cancellationToken);

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
            IWatchedTitleDataService watchedTitleService = scope.ServiceProvider.GetRequiredService<IWatchedTitleDataService>();

            // Früh abbrechen, falls bereits importiert
            Series? existing = await FindExistingSeriesAsync(seriesService, importSeries, cancellationToken);

            if (existing is not null)
            {
                _logger.Debug(() => $"Import übersprungen – bereits vorhanden: \"{importSeries.Title}\" ({importSeries.Source})");
                return existing.Id;
            }

            _logger.Info("Import gestartet: \"{Title}\" ({Source})", importSeries.Title, importSeries.Source);

            // Serie anlegen und persistieren – Id wird von EF nach SaveChanges gesetzt.
            // Früher überwachte Titel bekommen ihre Überwachung zurück (überlebt „Mediathek leeren").
            IReadOnlySet<string> watchedTitles = await watchedTitleService.GetAllAsync(cancellationToken);
            Series series = MapToSeries(importSeries, watchedTitles);
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

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar.
            // Beobachtet statt verworfen: Beim Beenden bricht der Token diese Aufgabe ab,
            // und eine unbeobachtete Abbruch-Ausnahme schlägt sonst erst später auf.
            DetachedTask.Observe(
                _coverCacheService.CacheCoversAsync(series.Id, uniqueEpisodes, ct: cancellationToken),
                _logger);

            return series.Id;
        }

        /// <summary>
        /// Importiert Episoden für eine bestehende Online-Serie neu vom Provider.
        /// Wird nach einer Migration aufgerufen, die alte Episoden bereinigt hat –
        /// der Nutzer muss nicht manuell neu importieren.
        /// Existierende Episoden der Serie werden nicht gelöscht; nur fehlende werden nachgeladen.
        /// Der Aufruf ist damit wiederholbar, ohne den Bestand zu verdoppeln.
        /// </summary>
        /// <param name="series">Die bestehende Serie mit gesetzter SpotifyArtistId oder AppleMusicArtistId.</param>
        /// <returns>Anzahl der neu angelegten Episoden. 0 wenn kein Provider zugeordnet oder keine Episoden gefunden.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> ReImportEpisodesAsync(Series series, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(series);

            using IServiceScope scope = _scopeFactory.CreateScope();

            (string providerKey, string sourceSeriesId)? resolved = await ResolveProviderForSeriesAsync(scope, series, cancellationToken);
            if (resolved is null)
            {
                _logger.Debug(() => $"Kein Provider für Serie \"{series.Title}\" – Re-Import übersprungen");
                return 0;
            }
            (string providerKey, string sourceSeriesId) = resolved.Value;

            _logger.Info("Re-Import gestartet: \"{Title}\" via {ProviderKey}", series.Title, providerKey);

            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(sourceSeriesId, cancellationToken: cancellationToken);
            List<ImportEpisode> uniqueEpisodes = DeduplicateBySourceEpisodeId(episodes, series.Title);

            // Der Bestand wird bewusst erst NACH dem Anbieter-Abruf gelesen. Der Abruf dauert bei
            // langen Serien Minuten; startet der Re-Import neben einem noch laufenden Erstimport,
            // sind dessen Folgen bis dahin geschrieben und werden hier erkannt. Ohne diesen
            // Abgleich legte der Re-Import jede Folge ein zweites Mal an.
            ExistingEpisodeIndex bestand = new(await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken));

            // Batch-Insert: ein einziger SaveChangesAsync-Aufruf statt N (analog ImportAsync).
            // Bei einer Serie mit 200 Folgen ersetzt das 200 DB-Roundtrips durch einen.
            List<Episode> mappedEpisodes = MapMissingEpisodes(uniqueEpisodes, bestand, series.Id, series.Title);

            if (mappedEpisodes.Count > 0)
            {
                await episodeService.AddRangeAsync(mappedEpisodes, cancellationToken);
            }

            int count = mappedEpisodes.Count;

            _logger.Info("Re-Import abgeschlossen: \"{Title}\", {EpisodeCount} Episoden nachgeladen", series.Title, count);

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
            if (count > 0)
            {
                DetachedTask.Observe(
                    _coverCacheService.CacheCoversAsync(series.Id, episodes, ct: cancellationToken),
                    _logger);
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

            using IServiceScope scope = _scopeFactory.CreateScope();

            (string providerKey, string sourceSeriesId)? resolved = await ResolveProviderForSeriesAsync(scope, series, cancellationToken);
            if (resolved is null) return 0;
            (string providerKey, string sourceSeriesId) = resolved.Value;

            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            // Bestehende Episoden in einen Index ziehen – ein einmaliger DB-Roundtrip,
            // statt der Schleife pro Treffer ein neues FirstOrDefault auf die Liste loszuwerfen.
            ExistingEpisodeIndex bestand = new(await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken));

            // Delta: bekannte Titel als Hinweis mitgeben. Die Quelle spart dadurch den teuren
            // Track-Lookup für bestehende Folgen (nur die Dauer bräuchte ihn), liefert deren
            // Metadaten inkl. Cover aber weiterhin – so kostet der Abgleich nur so viele Track-
            // Lookups wie es neue Folgen gibt, und fehlende Cover lassen sich trotzdem nachtragen.
            IReadOnlyList<ImportEpisode> providerEpisodes = await episodeSource.GetEpisodesAsync(
                sourceSeriesId,
                bestand.Titles,
                cancellationToken);

            // Add- und Update-Pfad getrennt sammeln; jeder Pfad löst genau einen DB-Roundtrip aus.
            List<Episode> newEpisodes = [];
            List<Episode> updatedEpisodes = [];

            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                // Anbieter-Kennung zuerst, Titel als Rückfall – ein umbenanntes Album bliebe
                // beim reinen Titelvergleich unerkannt und käme als zweite Zeile in den Bestand.
                if (bestand.TryFind(importEpisode, out Episode? existing))
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

                Episode neueFolge = MapToEpisode(importEpisode, series.Id);
                bestand.Add(neueFolge);
                newEpisodes.Add(neueFolge);
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
                DetachedTask.Observe(
                    _coverCacheService.CacheCoversAsync(series.Id, providerEpisodes, ct: cancellationToken),
                    _logger);
            }

            return newCount;
        }

        /// <summary>
        /// Bildet aus der Anbieter-Antwort die Folgen ab, die der Bestand noch nicht kennt.
        /// Jede angelegte Folge wandert sofort in den Index, damit sie kein zweites Mal entsteht.
        /// </summary>
        /// <param name="providerEpisodes">Die entdoppelte Anbieter-Antwort.</param>
        /// <param name="existing">Der vorhandene Bestand der Serie.</param>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
        /// <param name="seriesTitle">Serientitel für die Protokollzeile.</param>
        /// <returns>Die anzulegenden Folgen in Anbieter-Reihenfolge.</returns>
        private List<Episode> MapMissingEpisodes(
            List<ImportEpisode> providerEpisodes,
            ExistingEpisodeIndex existing,
            Guid seriesId,
            string seriesTitle)
        {
            List<Episode> missing = new(providerEpisodes.Count);
            int known = 0;

            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                if (existing.Contains(importEpisode))
                {
                    known++;
                    continue;
                }

                Episode mapped = MapToEpisode(importEpisode, seriesId);
                existing.Add(mapped);
                missing.Add(mapped);
            }

            if (known > 0)
            {
                _logger.Debug(() =>
                    $"Re-Import \"{seriesTitle}\": {known} bereits vorhandene Folgen übersprungen, {missing.Count} neu.");
            }

            return missing;
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
        /// Sucht die bereits online importierte Serie anhand der externen ID und Quelle.
        /// </summary>
        /// <remarks>
        /// Bewusst nur online importierte Serien: Eine lokal eingelesene Serie trägt dieselbe
        /// Künstlerkennung, sobald die Neuerscheinungs-Prüfung sie ermittelt und gespeichert hat.
        /// Zählte sie hier mit, gälte der Künstler als „bereits vorhanden" — die Suche blendete
        /// den Hinzufügen-Knopf aus und die Serie ließe sich nie in die Online-Mediathek holen.
        /// </remarks>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="service">Datendienst, über den nach der bestehenden Serie gesucht wird.</param>
        /// <param name="series">Das Anbieter-Modell mit Quelle und externer ID.</param>
        private static async Task<Series?> FindExistingSeriesAsync(ISeriesDataService service, ImportSeries series, CancellationToken cancellationToken = default)
        {
            return series.Source switch
            {
                ProviderKeys.Spotify => await service.GetOnlineImportedBySpotifyArtistIdAsync(series.SourceSeriesId, cancellationToken),
                ProviderKeys.AppleMusic => await service.GetOnlineImportedByAppleMusicArtistIdAsync(series.SourceSeriesId, cancellationToken),
                _ => null
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Series"/>-Entität aus einem <see cref="ImportSeries"/>-Modell.
        /// Setzt die provider-spezifische Artist-ID anhand der Source-Bezeichnung.
        /// Import und Abonnement sind dasselbe Konzept – jede importierte Serie ist direkt abonniert
        /// und erscheint sofort im Dashboard und in der Mediathek.
        /// </summary>
        /// <param name="importSeries">Das Anbieter-Modell, aus dem die Entität entsteht.</param>
        /// <param name="watchedTitles">Normalisierte Titel mit früher aktivierter Überwachung.</param>
        private static Series MapToSeries(ImportSeries importSeries, IReadOnlySet<string> watchedTitles)
        {
            return new Series
            {
                Title = importSeries.Title,
                Description = importSeries.Description,
                CoverImageUrl = importSeries.CoverImageUrl,
                SpotifyArtistId = importSeries.Source == ProviderKeys.Spotify ? importSeries.SourceSeriesId : null,
                AppleMusicArtistId = importSeries.Source == ProviderKeys.AppleMusic ? importSeries.SourceSeriesId : null,
                IsOnlineImported = true,
                IsSubscribed = true,
                IsWatched = watchedTitles.Contains(
                    EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(importSeries.Title))
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Episode"/>-Entität aus einem <see cref="ImportEpisode"/>-Modell.
        /// Setzt die provider-spezifische Album-ID anhand der Source-Bezeichnung.
        /// </summary>
        /// <param name="importEpisode">Das Anbieter-Modell, aus dem die Entität entsteht.</param>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
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
        /// <summary>
        /// Wählt für eine bestehende Serie den Provider, über den Delta-/Reimport laufen sollen.
        /// Spotify wird bevorzugt – aber nur, wenn es auch nutzbar ist: Ohne hinterlegte Credentials
        /// liefert Spotify keinen Token. In dem Fall wird auf Apple Music ausgewichen (öffentliche
        /// iTunes-API, ohne Credentials), sofern die Serie eine Apple-Music-ID hat. Ohne diesen
        /// Fallback lieferten Serien mit SpotifyArtistId ohne Credentials stillschweigend 0 neue
        /// Folgen – neue Folgen kamen nie an, obwohl Apple sie führt.
        /// </summary>
        /// <param name="scope">DI-Scope für den Credential-Store-Lookup.</param>
        /// <param name="series">Die bestehende Serie mit Provider-ID(s).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Provider-Schlüssel und Quell-ID, oder <see langword="null"/> wenn kein nutzbarer Provider vorliegt.</returns>
        private async Task<(string ProviderKey, string SourceSeriesId)?> ResolveProviderForSeriesAsync(
            IServiceScope scope,
            Series series,
            CancellationToken cancellationToken)
        {
            if (series.SpotifyArtistId is not null)
            {
                ISpotifyClientCredentialsProvider credentialsProvider =
                    scope.ServiceProvider.GetRequiredService<ISpotifyClientCredentialsProvider>();
                SpotifyClientCredentials? credentials = await credentialsProvider.GetAsync(cancellationToken).ConfigureAwait(false);

                if (credentials is not null)
                {
                    return (ProviderKeys.Spotify, series.SpotifyArtistId);
                }

                if (series.AppleMusicArtistId is not null)
                {
                    _logger.Warning(
                        "Spotify-Credentials fehlen — \"{Title}\" wird über Apple Music geprüft.", series.Title);
                    return (ProviderKeys.AppleMusic, series.AppleMusicArtistId);
                }

                _logger.Warning(
                    "Spotify-Credentials fehlen und keine Apple-Music-ID für \"{Title}\" — Prüfung nicht möglich.", series.Title);
                return null;
            }

            if (series.AppleMusicArtistId is not null)
            {
                return (ProviderKeys.AppleMusic, series.AppleMusicArtistId);
            }

            return null;
        }
    }
}
