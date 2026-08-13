using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Sucht Serien und Alben beim Anbieter.
    /// </summary>
    /// <remarks>
    /// Das Suchen und das Übernehmen in den eigenen Bestand sind zwei Dinge: Die Suche
    /// fragt eine fremde Gegenstelle und schreibt nichts, der Import schreibt und fragt
    /// niemanden. Sie standen in einer Klasse, weil beide denselben Anbieter kennen
    /// müssen.
    /// </remarks>
    internal sealed class ProviderSearch
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Richtet die Suche ein.
        /// </summary>
        /// <param name="scopeFactory">Für Einstellungen und den Anbieter-Zugang je Suchlauf.</param>
        /// <param name="logger">Protokollkanal.</param>
        public ProviderSearch(IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
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

    }
}
