using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.LocalLibrary.DependencyInjection
{
    /// <summary>
    /// Erweiterungsmethoden für die Registrierung der LocalLibrary-Dienste im DI-Container.
    /// </summary>
    public static class LocalLibraryServiceCollectionExtensions
    {
        /// <summary>
        /// Registriert alle Dienste des <c>EchoPlay.LocalLibrary</c>-Projekts als Scoped.
        /// Eingeschlossen: <see cref="ILocalLibraryScanner"/>, <see cref="IScanOrchestrator"/>,
        /// <see cref="TrackMatcher"/>, <see cref="AudioMetadataReader"/>, <see cref="CoverService"/>,
        /// <see cref="ILocalCoverLoader"/>, <see cref="ILocalCoverService"/>,
        /// <see cref="ICoverSearchService"/> und <see cref="IEpisodePatternAnalyzer"/>.
        /// </summary>
        /// <param name="services">Der <see cref="IServiceCollection"/>-Container.</param>
        /// <returns>Der erweiterte Container für Method-Chaining.</returns>
        public static IServiceCollection AddLocalLibrary(this IServiceCollection services)
        {
            _ = services.AddScoped<ILocalLibraryScanner, LocalLibraryScanner>();
            _ = services.AddScoped<IScanOrchestrator, ScanOrchestrator>();
            _ = services.AddScoped<ITrackMatcher, TrackMatcher>();
            _ = services.AddScoped<IAudioMetadataReader, AudioMetadataReader>();
            _ = services.AddScoped<ITagTitleReader, TagTitleReader>();
            _ = services.AddScoped<ILocalCoverLoader, LocalCoverLoader>();
            _ = services.AddScoped<ILocalCoverService, LocalCoverService>();
            _ = services.AddTransient<IEpisodePatternAnalyzer, EpisodePatternAnalyzer>();
            _ = services.AddScoped<IFolderRestructureService, FolderRestructureService>();

            // CoverService benötigt einen HttpClient – wird über IHttpClientFactory bereitgestellt
            _ = services.AddHttpClient<CoverService>();

            // ── Cover-Suche: fünf Anbieter, alle kostenlos und ohne API-Key ────────

            // Cover Art Archive (MusicBrainz) – Album-Cover, User-Agent ist Pflicht
            _ = services.AddHttpClient<CoverArtArchiveSearchService>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "EchoPlay/1.0 (https://ruhrcoder.de)");
            });

            // iTunes Search API – Album-Cover
            _ = services.AddHttpClient<ITunesCoverSearchService>();

            // Deezer Künstler-Profilbilder – ideal für Serien-Cover
            _ = services.AddHttpClient<DeezerArtistCoverSearchService>();

            // Deezer Album-Cover – dritte Quelle für Folgen-Cover
            _ = services.AddHttpClient<DeezerAlbumCoverSearchService>();

            // Discogs – physische Releases (CD, Kassette), User-Agent ist Pflicht.
            // Besonders wertvoll für ältere Hörspielserien, die auf Streaming-Plattformen fehlen.
            _ = services.AddHttpClient<DiscogsCoverSearchService>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "EchoPlay/1.0 (https://ruhrcoder.de)");
            });

            // Composite-Service: fasst alle Anbieter zusammen und fragt parallel ab.
            // Der Composite ist die einzige Implementierung von ICoverSearchService –
            // Konsumenten (ViewModel) bekommen automatisch alle Anbieter.
            // Reihenfolge: Künstlerbilder zuerst (Serien-Cover), dann Album-Cover.
            _ = services.AddTransient<ICoverSearchService>(provider =>
            {
                DeezerArtistCoverSearchService deezerArtists = provider.GetRequiredService<DeezerArtistCoverSearchService>();
                CoverArtArchiveSearchService musicBrainz = provider.GetRequiredService<CoverArtArchiveSearchService>();
                ITunesCoverSearchService iTunes = provider.GetRequiredService<ITunesCoverSearchService>();
                DeezerAlbumCoverSearchService deezerAlbums = provider.GetRequiredService<DeezerAlbumCoverSearchService>();
                DiscogsCoverSearchService discogs = provider.GetRequiredService<DiscogsCoverSearchService>();

                return new CompositeCoverSearchService([deezerArtists, musicBrainz, iTunes, deezerAlbums, discogs]);
            });

            return services;
        }
    }
}
