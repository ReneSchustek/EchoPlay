using EchoPlay.App.Services;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.Data.DependencyInjection;
using EchoPlay.LocalLibrary.DependencyInjection;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Configuration;
using EchoPlay.Spotify.DependencyInjection;
using EchoPlay.TagManager.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert die Quellen, aus denen EchoPlay seine Daten bezieht: Spotify,
    /// Apple Music, die lokale Mediathek, den Tag-Manager und die Datenbankdienste.
    /// </summary>
    internal static class ProviderRegistration
    {
        /// <summary>
        /// Registriert alle Datenquellen samt ihrer Anmeldung, Zwischenspeicher und
        /// Anwendungsfälle.
        /// </summary>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <param name="configuration">Die Konfiguration des Hosts; liefert die Basisadressen.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            AddSpotify(services, configuration);

            // Die iTunes-Suchschnittstelle ist kostenfrei und braucht weder Konfiguration
            // noch Anmeldung.
            _ = services.AddAppleMusicImport();

            // Folgenprüfung: vergleicht den lokalen Stand mit dem iTunes-Katalog. Die
            // Ergebnisse liegen in der Datenbank, der Prüfer selbst ist Scoped.
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker,
                EchoPlay.App.Services.OnlineEpisodeChecker>();

            _ = services.AddEchoPlayData();
            _ = services.AddLocalLibrary();
            _ = services.AddTagManager();

            return services;
        }

        /// <summary>
        /// Registriert Anmeldung, Token-Beschaffung und Schnittstellen-Client für Spotify.
        /// </summary>
        private static void AddSpotify(IServiceCollection services, IConfiguration configuration)
        {
            // Basisadressen aus der Konfiguration. Die Zugangsdaten stehen bewusst nicht
            // dort, sondern im verschlüsselten Speicher des Nutzers.
            SpotifyOptions baseOptions = new()
            {
                ApiBaseUrl = configuration.GetValue<string>("Spotify:ApiBaseUrl") ?? "https://api.spotify.com/v1/",
                AuthBaseUrl = configuration.GetValue<string>("Spotify:AuthBaseUrl") ?? "https://accounts.spotify.com/"
            };

            _ = services.AddSingleton<ISpotifyCredentialStore, SpotifyCredentialStore>();
            _ = services.AddSingleton<ISpotifyOptionsProvider>(provider =>
                new SpotifyOptionsProvider(baseOptions, provider.GetRequiredService<ISpotifyCredentialStore>()));
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider, SpotifyClientCredentialsProvider>();

            AddTokenClient(services, baseOptions);
            AddApiClient(services, baseOptions);

            // Anwendungsfälle samt der benannten Dienste, die der Import-Dienst auflöst.
            _ = services.AddSpotifyImport();
        }

        /// <summary>
        /// Registriert den Client für die Token-Anfrage. Die Zeitgrenze bleibt kurz — eine
        /// hängende Token-Anfrage blockiert jeden weiteren Aufruf der Schnittstelle.
        /// </summary>
        private static void AddTokenClient(IServiceCollection services, SpotifyOptions baseOptions)
        {
            IHttpClientBuilder tokenBuilder = services.AddHttpClient("SpotifyToken", client =>
            {
                client.BaseAddress = new(baseOptions.AuthBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            // Wie bei allen fremden Gegenstellen: ein vorübergehender Fehler beim
            // Token-Holen darf den Import nicht sofort abbrechen.
            _ = tokenBuilder.AddStandardResilienceHandler();
            HttpClientRegistration.AttachRequestLogging(tokenBuilder);

            // Singleton: Der Token-Zwischenspeicher lebt prozessweit, parallele Anforderungen
            // werden intern serialisiert. Die Zugangsdaten werden erst beim ersten Abruf
            // geholt — kein blockierendes Warten auf eine asynchrone Quelle.
            _ = services.AddSingleton<SpotifyTokenClient>();

            // Transient, damit jede Client-Instanz ihren eigenen Handler bekommt.
            _ = services.AddTransient<SpotifyAuthMessageHandler>();
        }

        /// <summary>
        /// Registriert den Client für die Web-Schnittstelle samt automatischer Anmeldung.
        /// </summary>
        /// <remarks>
        /// Die Wiederholungsregel wird zuerst angehängt und liegt damit außen, der
        /// Anmelde-Handler innen. So bekommt jeder Wiederholungsversuch einen frischen
        /// Token. Die Standardregel wiederholt nur 408/429/5xx und Zeitüberschreitungen —
        /// die 401-Erneuerung im Anmelde-Handler bleibt davon unberührt.
        /// </remarks>
        private static void AddApiClient(IServiceCollection services, SpotifyOptions baseOptions)
        {
            IHttpClientBuilder apiBuilder = services.AddHttpClient("SpotifyApi", client =>
            {
                client.BaseAddress = new(baseOptions.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            _ = apiBuilder.AddStandardResilienceHandler();
            _ = apiBuilder.AddHttpMessageHandler<SpotifyAuthMessageHandler>();
            HttpClientRegistration.AttachRequestLogging(apiBuilder);

            // Von Hand registriert, damit der benannte Client zum Zug kommt.
            _ = services.AddScoped<SpotifyApiClient>(provider =>
            {
                IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                HttpClient client = httpClientFactory.CreateClient("SpotifyApi");
                return new SpotifyApiClient(client, provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());
            });

            _ = services.AddScoped<ISpotifyApiClient>(provider => provider.GetRequiredService<SpotifyApiClient>());
        }
    }
}
