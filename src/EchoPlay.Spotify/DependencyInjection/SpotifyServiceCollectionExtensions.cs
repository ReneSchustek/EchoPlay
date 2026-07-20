using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;
using EchoPlay.Spotify.Scoring;
using EchoPlay.Spotify.Services;
using Microsoft.Extensions.DependencyInjection;

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
            _ = services.AddScoped<ISeriesImportSearch, SpotifySeriesImportSearch>();
            _ = services.AddScoped<IEpisodeImportSource, SpotifyEpisodeImportSource>();

            // Keyed-Registrierungen für ImportService – Schlüssel entspricht ProviderType.Spotify.ToString().
            _ = services.AddKeyedScoped<ISeriesImportSearch, SpotifySeriesImportSearch>("Spotify");
            _ = services.AddKeyedScoped<IEpisodeImportSource, SpotifyEpisodeImportSource>("Spotify");

            _ = services.AddOptions<SpotifyHoerspielSettings>();
            _ = services.AddScoped<HoerspielDecisionCache>();
            _ = services.AddScoped<SpotifyHoerspielAnalyzer>();
            _ = services.AddScoped<IHoerspielScorer<SpotifyArtistDto>, SpotifyHoerspielScorer>();

            _ = services.AddScoped<SpotifySeriesMapper>();

            return services;
        }
    }
}
