using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.LocalLibrary.DependencyInjection
{
    /// <summary>
    /// Erweiterungsmethoden für die Registrierung der LocalLibrary-Dienste im DI-Container.
    /// </summary>
    public static class LocalLibraryServiceCollectionExtensions
    {
        // MusicBrainz drosselt oder sperrt Anfragen ohne eigenen User-Agent und verlangt darin
        // Name, Version und eine Kontaktadresse.
        private const string UserAgent = "EchoPlay/1.0 (https://ruhrcoder.de)";

        /// <summary>
        /// Registriert alle Dienste des <c>EchoPlay.LocalLibrary</c>-Projekts. Die Dienste mit
        /// Zustand je Vorgang sind Scoped; die Cover-Suche und die Musteranalyse sind Transient,
        /// weil sie nichts zwischen zwei Aufrufen halten.
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
            _ = services.AddScoped<ITrackTitleResolver, TrackTitleResolver>();
            _ = services.AddScoped<ILocalCoverLoader, LocalCoverLoader>();
            _ = services.AddScoped<ILocalCoverService, LocalCoverService>();
            _ = services.AddTransient<IEpisodePatternAnalyzer, EpisodePatternAnalyzer>();
            _ = services.AddScoped<IFolderRestructureService, FolderRestructureService>();
            _ = services.AddScoped<IEpisodeFolderRenameService, EpisodeFolderRenameService>();

            _ = services.AddHttpClient<CoverService>();

            // Cover-Suche: vier Anbieter, alle kostenlos und ohne API-Schlüssel.
            _ = services.AddHttpClient<CoverArtArchiveSearchService>(client =>
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent));
            _ = services.AddHttpClient<ITunesCoverSearchService>();
            _ = services.AddHttpClient<DeezerArtistCoverSearchService>();
            _ = services.AddHttpClient<DeezerAlbumCoverSearchService>();

            // Unter ICoverSearchService steht nur der Composite; wer die Schnittstelle anfordert,
            // fragt so alle Anbieter auf einmal. Die Reihenfolge ist die der Anzeige:
            // Künstlerbilder zuerst, weil sie als Serien-Cover am ehesten passen.
            _ = services.AddTransient<ICoverSearchService>(provider =>
            {
                DeezerArtistCoverSearchService deezerArtists = provider.GetRequiredService<DeezerArtistCoverSearchService>();
                CoverArtArchiveSearchService musicBrainz = provider.GetRequiredService<CoverArtArchiveSearchService>();
                ITunesCoverSearchService iTunes = provider.GetRequiredService<ITunesCoverSearchService>();
                DeezerAlbumCoverSearchService deezerAlbums = provider.GetRequiredService<DeezerAlbumCoverSearchService>();

                return new CompositeCoverSearchService(
                    [deezerArtists, musicBrainz, iTunes, deezerAlbums],
                    provider.GetRequiredService<ILoggerFactory>());
            });

            return services;
        }
    }
}
