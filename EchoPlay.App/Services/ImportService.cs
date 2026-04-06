using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
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
            _scopeFactory = scopeFactory;
            _coverCacheService = coverCacheService;
            _logger = loggerFactory.CreateLogger("ImportService");
        }

        /// <summary>
        /// Sucht nach Hörspielserien beim aktiven Provider laut AppSettings.
        /// Gibt eine leere Liste zurück, wenn keine Ergebnisse gefunden werden.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <returns>Fachlich bewertete Liste importierbarer Serien.</returns>
        /// <exception cref="ArgumentException">Wird geworfen, wenn <paramref name="query"/> leer oder nur Leerzeichen enthält.</exception>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            using IServiceScope scope = _scopeFactory.CreateScope();

            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();

            // Ohne aktiven Provider kann keine Online-Suche stattfinden.
            if (settings.ActiveProvider == ProviderType.None)
            {
                return [];
            }

            // Provider-Schlüssel entspricht dem Enum-Namen ("Spotify" / "AppleMusic")
            string providerKey = settings.ActiveProvider.ToString();
            _logger.Debug($"Suche nach \"{query}\" via {providerKey}");
            ISeriesImportSearch search = scope.ServiceProvider.GetRequiredKeyedService<ISeriesImportSearch>(providerKey);

            return await search.SearchAsync(query);
        }

        /// <summary>
        /// Sucht beim aktiven Provider nach Alben (einzelnen Folgen) anhand eines Suchbegriffs.
        /// Ergänzt die Seriensuche (<see cref="SearchAsync"/>) um die Möglichkeit,
        /// gezielt nach Folgentiteln zu suchen (z.B. "Kapatenhund").
        /// </summary>
        /// <param name="query">Suchbegriff – wird an die Provider-API weitergereicht.</param>
        /// <returns>Album-Ergebnisse als <see cref="ImportSeries"/> mit <c>IsAlbumResult = true</c>.</returns>
        public async Task<IReadOnlyList<ImportSeries>> SearchAlbumsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return [];
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();

            if (settings.ActiveProvider == ProviderType.None)
            {
                return [];
            }

            _logger.Debug($"Album-Suche nach \"{query}\" via {settings.ActiveProvider}");

            List<ImportSeries> results = [];

            if (settings.ActiveProvider == ProviderType.Spotify)
            {
                EchoPlay.Spotify.Abstractions.ISpotifyApiClient? spotifyClient =
                    scope.ServiceProvider.GetService<EchoPlay.Spotify.Abstractions.ISpotifyApiClient>();

                if (spotifyClient is null) return [];

                IReadOnlyList<EchoPlay.Spotify.Dtos.SpotifyAlbumDto> albums =
                    await spotifyClient.SearchAlbumsAsync(query, 15);

                foreach (EchoPlay.Spotify.Dtos.SpotifyAlbumDto album in albums)
                {
                    results.Add(new ImportSeries
                    {
                        SourceSeriesId = album.SpotifyAlbumId,
                        Source         = "Spotify",
                        Title          = album.Title,
                        ArtistName     = album.ArtistName,
                        CoverImageUrl  = album.ImageUrl,
                        IsAlbumResult  = true,
                        IsHoerspiel    = true,
                        Score          = 50
                    });
                }
            }
            else if (settings.ActiveProvider == ProviderType.AppleMusic)
            {
                EchoPlay.AppleMusic.Abstractions.IAppleMusicSearchClient? appleClient =
                    scope.ServiceProvider.GetService<EchoPlay.AppleMusic.Abstractions.IAppleMusicSearchClient>();

                if (appleClient is null) return [];

                EchoPlay.AppleMusic.Dtos.ITunesResponseDto<EchoPlay.AppleMusic.Dtos.ITunesCollectionDto> response =
                    await appleClient.SearchAlbumsAsync(query, 15);

                foreach (EchoPlay.AppleMusic.Dtos.ITunesCollectionDto album in response.Results)
                {
                    if (!string.Equals(album.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(new ImportSeries
                    {
                        SourceSeriesId = album.CollectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Source         = "AppleMusic",
                        Title          = album.CollectionName,
                        ArtistName     = album.ArtistName,
                        CoverImageUrl  = null,
                        IsAlbumResult  = true,
                        IsHoerspiel    = true,
                        Score          = 50
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Prüft, ob eine Serie bereits in der Datenbank vorhanden ist (via SourceSeriesId).
        /// </summary>
        /// <param name="series">Die zu prüfende ImportSerie.</param>
        /// <returns>True wenn die Serie bereits importiert wurde.</returns>
        public async Task<bool> IsAlreadyImportedAsync(ImportSeries series)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            return await FindExistingSeriesAsync(seriesService, series) is not null;
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
        public async Task<Guid> ImportAsync(ImportSeries importSeries, IProgress<string>? progress = null)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            // Früh abbrechen, falls bereits importiert
            Series? existing = await FindExistingSeriesAsync(seriesService, importSeries);

            if (existing is not null)
            {
                _logger.Debug($"Import übersprungen – bereits vorhanden: \"{importSeries.Title}\" ({importSeries.Source})");
                return existing.Id;
            }

            _logger.Info($"Import gestartet: \"{importSeries.Title}\" ({importSeries.Source})");

            // Serie anlegen und persistieren – Id wird von EF nach SaveChanges gesetzt
            Series series = MapToSeries(importSeries);
            await seriesService.AddAsync(series);

            // Episoden laden – bei großen Serien (>100 Episoden) kann dieser HTTP-Aufruf mehrere Sekunden dauern
            progress?.Report($"Lade Episoden für \"{importSeries.Title}\" \u2026");
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(importSeries.Source);
            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(importSeries.SourceSeriesId);

            progress?.Report($"Speichere Episoden \u2026 (0/{episodes.Count})");

            for (int i = 0; i < episodes.Count; i++)
            {
                Episode episode = MapToEpisode(episodes[i], series.Id);
                await episodeService.AddAsync(episode);

                // Nicht bei jeder Folge melden – zu viele UI-Updates blockieren den Dispatcher
                if (i > 0 && i % 10 == 0)
                {
                    progress?.Report($"Speichere Episoden \u2026 ({i}/{episodes.Count})");
                }
            }

            _logger.Info($"Import abgeschlossen: \"{importSeries.Title}\", {episodes.Count} Episoden");

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
            _ = _coverCacheService.CacheCoversAsync(series.Id, episodes);

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
        public async Task<int> ReImportEpisodesAsync(Series series)
        {
            // Provider-Schlüssel aus der Serie ableiten – Spotify hat Vorrang
            string? providerKey = series.SpotifyArtistId is not null ? "Spotify"
                                : series.AppleMusicArtistId is not null ? "AppleMusic"
                                : null;

            if (providerKey is null)
            {
                _logger.Debug($"Kein Provider für Serie \"{series.Title}\" – Re-Import übersprungen");
                return 0;
            }

            // Die Artist-ID ist gleichzeitig die SourceSeriesId für den Provider
            string sourceSeriesId = providerKey == "Spotify"
                ? series.SpotifyArtistId!
                : series.AppleMusicArtistId!;

            _logger.Info($"Re-Import gestartet: \"{series.Title}\" via {providerKey}");

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            IReadOnlyList<ImportEpisode> episodes = await episodeSource.GetEpisodesAsync(sourceSeriesId);

            int count = 0;

            foreach (ImportEpisode importEpisode in episodes)
            {
                Episode episode = MapToEpisode(importEpisode, series.Id);
                await episodeService.AddAsync(episode);
                count++;
            }

            _logger.Info($"Re-Import abgeschlossen: \"{series.Title}\", {count} Episoden nachgeladen");

            // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
            if (count > 0)
            {
                _ = _coverCacheService.CacheCoversAsync(series.Id, episodes);
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
        public async Task<int> DeltaImportEpisodesAsync(Series series)
        {
            string? providerKey = series.SpotifyArtistId is not null ? "Spotify"
                                : series.AppleMusicArtistId is not null ? "AppleMusic"
                                : null;

            if (providerKey is null) return 0;

            string sourceSeriesId = providerKey == "Spotify"
                ? series.SpotifyArtistId!
                : series.AppleMusicArtistId!;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IEpisodeImportSource episodeSource = scope.ServiceProvider.GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

            // Bestehende Episoden-Titel sammeln – dient als Duplikaterkennung
            IReadOnlyList<Episode> existingEpisodes = await episodeService.GetBySeriesIdAsync(series.Id);
            HashSet<string> existingTitles = new(existingEpisodes.Count, StringComparer.OrdinalIgnoreCase);

            foreach (Episode existing in existingEpisodes)
            {
                existingTitles.Add(existing.Title);
            }

            IReadOnlyList<ImportEpisode> providerEpisodes = await episodeSource.GetEpisodesAsync(sourceSeriesId);

            int newCount = 0;

            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                // Titel-basierter Vergleich – robuster als Nummernvergleich,
                // da Online-Episoden nicht immer eine konsistente Folgennummer haben
                if (existingTitles.Contains(importEpisode.Title)) continue;

                Episode episode = MapToEpisode(importEpisode, series.Id);
                await episodeService.AddAsync(episode);
                newCount++;
            }

            if (newCount > 0)
            {
                _logger.Info($"Delta-Import: {newCount} neue Episoden für \"{series.Title}\"");

                // Cover im Hintergrund laden – Provider-URLs sind nur hier verfügbar
                _ = _coverCacheService.CacheCoversAsync(series.Id, providerEpisodes);
            }

            return newCount;
        }

        /// <summary>
        /// Sucht eine bestehende Serie anhand der externen ID und Quelle.
        /// </summary>
        private static async Task<Series?> FindExistingSeriesAsync(ISeriesDataService service, ImportSeries series)
        {
            return series.Source switch
            {
                "Spotify"    => await service.GetBySpotifyArtistIdAsync(series.SourceSeriesId),
                "AppleMusic" => await service.GetByAppleMusicArtistIdAsync(series.SourceSeriesId),
                _            => null
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Series"/>-Entität aus einem <see cref="ImportSeries"/>-Modell.
        /// Setzt die provider-spezifische Artist-ID anhand der Source-Bezeichnung.
        /// Import und Abonnement sind dasselbe Konzept – jede importierte Serie ist direkt abonniert
        /// und erscheint sofort im Dashboard und in der Mediathek.
        /// </summary>
        private static Series MapToSeries(ImportSeries importSeries)
        {
            return new Series
            {
                Title              = importSeries.Title,
                Description        = importSeries.Description,
                CoverImageUrl      = importSeries.CoverImageUrl,
                SpotifyArtistId    = importSeries.Source == "Spotify"    ? importSeries.SourceSeriesId : null,
                AppleMusicArtistId = importSeries.Source == "AppleMusic" ? importSeries.SourceSeriesId : null,
                IsOnlineImported   = true,
                IsSubscribed       = true
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Episode"/>-Entität aus einem <see cref="ImportEpisode"/>-Modell.
        /// </summary>
        private static Episode MapToEpisode(ImportEpisode importEpisode, Guid seriesId)
        {
            return new Episode
            {
                SeriesId      = seriesId,
                Title         = importEpisode.Title,
                EpisodeNumber = importEpisode.EpisodeNumber,
                ReleaseDate   = importEpisode.ReleaseDate,
                Duration      = importEpisode.Duration,
                ProviderUrl   = importEpisode.ProviderUrl,
                CoverImageUrl = importEpisode.CoverImageUrl,
            };
        }

    }
}
