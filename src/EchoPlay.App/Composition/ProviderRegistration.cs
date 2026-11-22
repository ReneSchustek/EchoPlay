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
            // Basisadressen aus der Konfiguration, sonst die Vorgaben der Optionen. Die Zugangsdaten
            // stehen bewusst nicht dort, sondern im verschlüsselten Speicher des Nutzers. Eine
            // unlesbare Adresse in der Konfiguration bricht den Start ab; sie ist ein Fehler der
            // Auslieferung, kein Zustand, mit dem die Anwendung weiterlaufen sollte.
            SpotifyOptions defaults = new();
            SpotifyOptions baseOptions = new()
            {
                ApiBaseUrl = configuration.GetValue<Uri>("Spotify:ApiBaseUrl") ?? defaults.ApiBaseUrl,
                AuthBaseUrl = configuration.GetValue<Uri>("Spotify:AuthBaseUrl") ?? defaults.AuthBaseUrl
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
        /// Grenze je Versuch der Token-Anfrage. Die Antwort ist ein kurzes JSON; was nach
        /// fünf Sekunden nicht da ist, hängt.
        /// </summary>
        internal static readonly TimeSpan TokenAttemptTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gesamtgrenze der Token-Anfrage samt Wiederholungen. Bewusst kürzer als bei den
        /// übrigen Anfragen: Eine hängende Token-Anfrage hält jeden weiteren Aufruf der
        /// Schnittstelle auf, denn alle warten auf denselben Token.
        /// </summary>
        internal static readonly TimeSpan TokenTotalTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Registriert den Client für die Token-Anfrage mit kurzer Zeitgrenze
        /// (<see cref="TokenAttemptTimeout"/>, <see cref="TokenTotalTimeout"/>).
        /// </summary>
        private static void AddTokenClient(IServiceCollection services, SpotifyOptions baseOptions)
        {
            IHttpClientBuilder tokenBuilder = services.AddHttpClient("SpotifyToken", client =>
            {
                client.BaseAddress = baseOptions.AuthBaseUrl;
            });

            // Wie bei allen fremden Gegenstellen: ein vorübergehender Fehler beim
            // Token-Holen darf den Import nicht sofort abbrechen.
            HttpClientRegistration.AttachResilience(tokenBuilder, HostThrottling.None, options =>
            {
                options.AttemptTimeout.Timeout = TokenAttemptTimeout;
                options.TotalRequestTimeout.Timeout = TokenTotalTimeout;
            });

            // Singleton: Der Token-Zwischenspeicher lebt prozessweit, parallele Anforderungen
            // werden intern serialisiert. Die Zugangsdaten werden erst beim ersten Abruf
            // geholt, also kein blockierendes Warten auf eine asynchrone Quelle.
            _ = services.AddSingleton<SpotifyTokenClient>();

            // Transient, damit jede Client-Instanz ihren eigenen Handler bekommt.
            _ = services.AddTransient<SpotifyAuthMessageHandler>();
        }

        /// <summary>
        /// Registriert den Client für die Web-Schnittstelle samt automatischer Anmeldung.
        /// Es gelten die Grenzen der fremden Schnittstellen
        /// (<see cref="HttpClientRegistration.ProviderTotalTimeout"/>).
        /// </summary>
        /// <remarks>
        /// Die Wiederholungsregel wird zuerst angehängt und liegt damit außen, der
        /// Anmelde-Handler innen. So bekommt jeder Wiederholungsversuch einen frischen
        /// Token. Die Standardregel wiederholt nur 408/429/5xx und Zeitüberschreitungen;
        /// die 401-Erneuerung im Anmelde-Handler bleibt davon unberührt.
        /// </remarks>
        private static void AddApiClient(IServiceCollection services, SpotifyOptions baseOptions)
        {
            IHttpClientBuilder apiBuilder = services.AddHttpClient("SpotifyApi", client =>
            {
                client.BaseAddress = baseOptions.ApiBaseUrl;
            });

            HttpClientRegistration.AttachRetryPipeline(apiBuilder);
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
