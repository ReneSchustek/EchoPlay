using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;
using EchoPlay.Spotify.Scoring;
using EchoPlay.Spotify.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace EchoPlay.Spotify.DependencyInjection
{
    /// <summary>
    /// Erweiterungen zur Registrierung der Spotify-Integration im Dependency-Injection-Container.
    /// </summary>
    public static class SpotifyServiceCollectionExtensions
    {
        /// <summary>
        /// Registriert alle für den Spotify-Import benötigten Services und Abhängigkeiten.
        /// </summary>
        /// <param name="services">Die Service-Sammlung.</param>
        /// <returns>Die erweiterte Service-Sammlung.</returns>
        public static IServiceCollection AddSpotifyImport(this IServiceCollection services)
        {
            services.AddScoped<ISeriesImportSearch, SpotifySeriesImportSearch>();
            services.AddScoped<IEpisodeImportSource, SpotifyEpisodeImportSource>();

            // Keyed-Registrierungen für ImportService – Schlüssel entspricht ProviderType.Spotify.ToString().
            // Der Typ-Check ist hier sicher, weil wir uns in derselben Assembly befinden.
            services.AddKeyedScoped<ISeriesImportSearch>("Spotify",
                (sp, _) => sp.GetServices<ISeriesImportSearch>()
                    .First(s => s is SpotifySeriesImportSearch));
            services.AddKeyedScoped<IEpisodeImportSource>("Spotify",
                (sp, _) => sp.GetServices<IEpisodeImportSource>()
                    .First(s => s is SpotifyEpisodeImportSource));

            services.AddOptions<SpotifyHoerspielSettings>();
            services.AddScoped<HoerspielDecisionCache>();
            services.AddScoped<SpotifyHoerspielAnalyzer>();
            services.AddScoped<IHoerspielScorer<SpotifyArtistDto>, SpotifyHoerspielScorer>();

            services.AddScoped<SpotifySeriesMapper>();

            return services;
        }
    }
}
