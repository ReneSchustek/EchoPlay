using EchoPlay.App.Infrastructure;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

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

        // Die Suche beim Anbieter schreibt nichts und gehört deshalb nicht
        // in denselben Typ wie der Import.
        private readonly ProviderSearch _providerSearch;
        private readonly ImportEpisodeMapper _mapper;
        private readonly SeriesProviderResolver _providerResolver;

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
            _mapper = new ImportEpisodeMapper(_logger);
            _providerResolver = new SeriesProviderResolver(_logger);
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
        /// Sucht Alben beim Anbieter: einzelne Veröffentlichungen statt ganzer Serien.
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
        /// Meldet Texte wie „Lade Episoden …“ und „Speichere Episoden … (250)“ in der Sprache der Oberfläche.
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
            IWatchedTitleDataService watchedTitleService = scope.ServiceProvider.GetRequiredService<IWatchedTitleDataService>();

            // Früh abbrechen, falls bereits importiert
            Series? existing = await FindExistingSeriesAsync(seriesService, importSeries, cancellationToken);

            if (existing is not null)
            {
                _logger.Debug(() => $"Import übersprungen – bereits vorhanden: \"{importSeries.Title}\" ({importSeries.Source})");
                return existing.Id;
            }

            _logger.Info("Import gestartet: \"{Title}\" ({Source})", importSeries.Title, importSeries.Source);

            // Serie anlegen und persistieren. Die Id wird von EF nach SaveChanges gesetzt.
            // Früher überwachte Titel bekommen ihre Überwachung zurück (überlebt „Mediathek leeren").
            IReadOnlySet<string> watchedTitles = await watchedTitleService.GetAllAsync(cancellationToken);
            Series series = ImportEpisodeMapper.MapToSeries(importSeries, watchedTitles);
            await seriesService.AddAsync(series, cancellationToken);

            List<ImportEpisode> uniqueEpisodes = await ImportEpisodesOfNewSeriesAsync(
                scope.ServiceProvider, importSeries, series.Id, progress, cancellationToken);

            _logger.Info("Import abgeschlossen: \"{Title}\", {EpisodeCount} Episoden", importSeries.Title, uniqueEpisodes.Count);

            CacheCoversInBackground(series.Id, uniqueEpisodes, cancellationToken);
            return series.Id;
        }

        /// <summary>
        /// Lädt die Cover im Hintergrund, solange die Antwort des Anbieters mit ihren Adressen vorliegt.
        /// </summary>
        private void CacheCoversInBackground(Guid seriesId, IReadOnlyList<ImportEpisode> episodes, CancellationToken cancellationToken)
        {
            // Beobachtet statt verworfen: Beim Beenden bricht der Token diese Aufgabe ab,
            // und eine unbeobachtete Abbruch-Ausnahme schlägt sonst erst später auf.
            DetachedTask.Observe(
                _coverCacheService.CacheCoversAsync(seriesId, episodes, ct: cancellationToken),
                _logger);
        }

        /// <summary>
        /// Lädt die Folgen beim Anbieter, verwirft doppelte und speichert den Rest in einem Zug.
        /// </summary>
        /// <returns>Die gespeicherten Folgen, mit ihren Cover-Adressen für den Hintergrund.</returns>
        private async Task<List<ImportEpisode>> ImportEpisodesOfNewSeriesAsync(
            IServiceProvider services,
            ImportSeries importSeries,
            Guid seriesId,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            // Episoden laden. Bei großen Serien (>100 Episoden) kann dieser HTTP-Aufruf mehrere
            // Sekunden dauern. Der Text steht auf der Importseite und in der Statusleiste.
            progress?.Report(FormatProgress("ImportProgressLoadingEpisodes", "Lade Episoden für „{0}“ …", importSeries.Title));
            IEpisodeImportSource episodeSource = services.GetRequiredKeyedService<IEpisodeImportSource>(importSeries.Source);
            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(importSeries.SourceSeriesId, cancellationToken: cancellationToken);

            // Schutzgitter: doppelte SourceEpisodeIds (Provider-Duplikate, Re-Releases,
            // Compilation-Alben mit identischer CollectionId) werden hier idempotent verworfen,
            // bevor der Insert läuft. Ohne diese Stufe entstünden bei mehrfacher Auslieferung
            // derselben Folge mehrere Episode-Zeilen mit identischer Provider-ID.
            List<ImportEpisode> uniqueEpisodes = _mapper.DeduplicateBySourceEpisodeId(episodes, importSeries.Title);

            progress?.Report(FormatProgress("ImportProgressSavingEpisodes", "Speichere Episoden … ({0})", uniqueEpisodes.Count));

            // Batch-Insert: ein einziger SaveChangesAsync-Aufruf statt N. Bei einer
            // Hörspielserie mit 200 Folgen ersetzt das 200 DB-Roundtrips durch einen.
            List<Episode> mappedEpisodes = [.. uniqueEpisodes.Select(episode => ImportEpisodeMapper.MapToEpisode(episode, seriesId))];
            await services.GetRequiredService<IEpisodeDataService>().AddRangeAsync(mappedEpisodes, cancellationToken);

            return uniqueEpisodes;
        }

        /// <summary>
        /// Importiert Episoden für eine bestehende Online-Serie neu vom Provider.
        /// Die Online-Mediathek ruft das für jede Serie ohne Folgen auf, etwa nach einer
        /// Migration, die alte Episoden bereinigt hat; der Nutzer muss nicht manuell neu importieren.
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

            (string providerKey, string sourceSeriesId)? resolved = await _providerResolver.ResolveAsync(scope, series, cancellationToken);
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
            List<ImportEpisode> uniqueEpisodes = _mapper.DeduplicateBySourceEpisodeId(episodes, series.Title);

            // Der Bestand wird erst NACH dem Anbieter-Abruf gelesen. Der Abruf dauert bei
            // langen Serien Minuten; startet der Re-Import neben einem noch laufenden Erstimport,
            // sind dessen Folgen bis dahin geschrieben und werden hier erkannt. Ohne diesen
            // Abgleich legte der Re-Import jede Folge ein zweites Mal an.
            ExistingEpisodeIndex existingIndex = new(await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken));

            // Batch-Insert: ein einziger SaveChangesAsync-Aufruf statt N (analog ImportAsync).
            // Bei einer Serie mit 200 Folgen ersetzt das 200 DB-Roundtrips durch einen.
            List<Episode> mappedEpisodes = _mapper.MapMissingEpisodes(uniqueEpisodes, existingIndex, series.Id, series.Title);

            if (mappedEpisodes.Count > 0)
            {
                await episodeService.AddRangeAsync(mappedEpisodes, cancellationToken);
            }

            int count = mappedEpisodes.Count;

            _logger.Info("Re-Import abgeschlossen: \"{Title}\", {EpisodeCount} Episoden nachgeladen", series.Title, count);

            if (count > 0)
            {
                CacheCoversInBackground(series.Id, episodes, cancellationToken);
            }

            return count;
        }

        /// <summary>
        /// Prüft eine Online-Serie auf neue Folgen beim Provider und importiert nur die Differenz.
        /// Bereits vorhandene Episoden (Titelvergleich) werden übersprungen.
        /// Geeignet für regelmäßige Aktualisierung, deutlich schneller als ein Voll-Import.
        /// </summary>
        /// <param name="series">Die bestehende Serie mit gesetzter Provider-ID.</param>
        /// <returns>Anzahl der neu importierten Episoden. 0 wenn keine neuen gefunden.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> DeltaImportEpisodesAsync(Series series, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(series);

            using IServiceScope scope = _scopeFactory.CreateScope();

            (string providerKey, string sourceSeriesId)? resolved = await _providerResolver.ResolveAsync(scope, series, cancellationToken);
            if (resolved is null) return 0;
            (string providerKey, string sourceSeriesId) = resolved.Value;

            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            // Bestehende Episoden in einen Index ziehen: ein einmaliger DB-Roundtrip,
            // statt der Schleife pro Treffer ein neues FirstOrDefault auf die Liste loszuwerfen.
            ExistingEpisodeIndex existingIndex = new(await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken));

            // Delta: bekannte Titel als Hinweis mitgeben. Die Quelle spart dadurch den teuren
            // Track-Lookup für bestehende Folgen (nur die Dauer bräuchte ihn), liefert deren
            // Metadaten inkl. Cover aber weiterhin. So kostet der Abgleich nur so viele Track-
            // Lookups wie es neue Folgen gibt, und fehlende Cover lassen sich trotzdem nachtragen.
            IReadOnlyList<ImportEpisode> providerEpisodes = await episodeSource.GetEpisodesAsync(
                sourceSeriesId,
                existingIndex.Titles,
                cancellationToken);

            (List<Episode> newEpisodes, List<Episode> updatedEpisodes) = SplitDelta(providerEpisodes, existingIndex, series.Id);
            await SaveDeltaAsync(episodeService, newEpisodes, updatedEpisodes, cancellationToken);

            int newCount = newEpisodes.Count;
            if (newCount > 0)
            {
                _logger.Info("Delta-Import: {NewCount} neue Episoden für \"{Title}\"", newCount, series.Title);
                CacheCoversInBackground(series.Id, providerEpisodes, cancellationToken);
            }

            return newCount;
        }

        /// <summary>Schreibt neue und ergänzte Folgen; jeder Pfad löst genau einen DB-Roundtrip aus.</summary>
        private static async Task SaveDeltaAsync(
            IEpisodeDataService episodeService,
            List<Episode> newEpisodes,
            List<Episode> updatedEpisodes,
            CancellationToken cancellationToken)
        {
            if (newEpisodes.Count > 0)
            {
                await episodeService.AddRangeAsync(newEpisodes, cancellationToken);
            }

            if (updatedEpisodes.Count > 0)
            {
                await episodeService.UpdateRangeAsync(updatedEpisodes, cancellationToken);
            }
        }

        /// <summary>
        /// Teilt die Antwort des Anbieters in neue Folgen und bestehende, denen nur das Cover fehlte.
        /// Anbieter-Kennung zuerst, Titel als Rückfall, denn ein umbenanntes Album bliebe beim reinen
        /// Titelvergleich unerkannt und käme als zweite Zeile in den Bestand.
        /// </summary>
        private static (List<Episode> New, List<Episode> Updated) SplitDelta(
            IReadOnlyList<ImportEpisode> providerEpisodes, ExistingEpisodeIndex existingIndex, Guid seriesId)
        {
            List<Episode> newEpisodes = [];
            List<Episode> updatedEpisodes = [];
            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                if (!existingIndex.TryFind(importEpisode, out Episode? existing))
                {
                    Episode newEpisode = ImportEpisodeMapper.MapToEpisode(importEpisode, seriesId);
                    existingIndex.Add(newEpisode);
                    newEpisodes.Add(newEpisode);
                }
                else if (importEpisode.CoverImageUrl is not null && string.IsNullOrEmpty(existing.CoverImageUrl))
                {
                    // Bestehende Episode: CoverImageUrl nachtragen, falls noch nicht gesetzt
                    existing.CoverImageUrl = importEpisode.CoverImageUrl.OriginalString;
                    updatedEpisodes.Add(existing);
                }
            }

            return (newEpisodes, updatedEpisodes);
        }

        /// <summary>
        /// Sucht die bereits online importierte Serie anhand der externen ID und Quelle.
        /// </summary>
        /// <remarks>
        /// Bewusst nur online importierte Serien: Eine lokal eingelesene Serie trägt dieselbe
        /// Künstlerkennung, sobald die Neuerscheinungs-Prüfung sie ermittelt und gespeichert hat.
        /// Zählte sie hier mit, gälte der Künstler als „bereits vorhanden". Die Suche blendete
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
        /// Setzt eine Fortschrittszeile aus der Vorlage der Ressourcen zusammen.
        /// </summary>
        /// <param name="key">Schlüssel der Vorlage mit einem Platzhalter.</param>
        /// <param name="fallback">Vorlage, wenn die Ressourcen fehlen (Testprozess).</param>
        /// <param name="value">Wert für den Platzhalter.</param>
        private static string FormatProgress(string key, string fallback, object value)
        {
            string format = EchoPlay.App.Helpers.SafeResourceLoader.Get(key, fallback);
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
        }
    }
}
