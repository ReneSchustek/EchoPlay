using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Scoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EchoPlay.AppleMusic.DependencyInjection
{
    /// <summary>
    /// Registriert die Apple-Music-Import-Use-Cases.
    /// Nutzt intern die kostenfreie iTunes Search API statt der kostenpflichtigen Apple Music Developer API.
    /// Es wird keine Authentifizierung benötigt.
    /// </summary>
    public static class AppleMusicServiceCollectionExtensions
    {
        /// <summary>
        /// Fügt die Apple-Music-Importfunktionalität dem DI-Container hinzu.
        /// Registriert den iTunes-Search-Client mit vorkonfigurierter BaseAddress.
        /// </summary>
        /// <param name="services">Die Service-Sammlung.</param>
        /// <returns>Die erweiterte Service-Sammlung.</returns>
        public static IServiceCollection AddAppleMusicImport(this IServiceCollection services)
        {
            // HttpClient mit BaseAddress und Timeout für die iTunes Search API.
            // 15 Sekunden sind für eine Desktop-App das Maximum – danach ist die UX beschädigt.
            _ = services.AddHttpClient<IAppleMusicSearchClient, AppleMusicSearchClient>(client =>
            {
                client.BaseAddress = new Uri("https://itunes.apple.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            _ = services.AddScoped<ISeriesImportSearch, AppleMusicSeriesSearch>();
            _ = services.AddScoped<IEpisodeImportSource, AppleMusicEpisodeSource>();

            // Keyed-Registrierungen für ImportService – Schlüssel entspricht ProviderType.AppleMusic.ToString().
            _ = services.AddKeyedScoped<ISeriesImportSearch, AppleMusicSeriesSearch>("AppleMusic");
            _ = services.AddKeyedScoped<IEpisodeImportSource, AppleMusicEpisodeSource>("AppleMusic");

            _ = services.AddOptions<AppleMusicHoerspielSettings>();
            services.TryAddScoped<HoerspielDecisionCache>();
            _ = services.AddScoped<AppleMusicHoerspielAnalyzer>();
            _ = services.AddScoped<IHoerspielScorer<ITunesArtistDto>, AppleMusicHoerspielScorer>();

            return services;
        }
    }
}
