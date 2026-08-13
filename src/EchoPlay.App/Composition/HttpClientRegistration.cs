using EchoPlay.App.Services;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using System;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert alle HTTP-Clients der Anwendung an einer Stelle. Damit gelten
    /// einheitliche Zeitgrenzen, Kennungen und Wiederholungsregeln, statt dass jeder
    /// Verbraucher eine eigene Instanz hält.
    /// </summary>
    internal static class HttpClientRegistration
    {
        /// <summary>
        /// Registriert die benannten Clients für Cover-Abruf, Erreichbarkeitsprüfung,
        /// Aktualisierungsprüfung und Aktualisierungs-Download.
        /// </summary>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayHttpClients(this IServiceCollection services)
        {
            AddCoverDownloadClient(services);
            AddOnlineCheckClient(services);
            AddUpdateDownloadClient(services);
            AddUpdateCheckClient(services);
            return services;
        }

        /// <summary>
        /// Hängt Wiederholungsregeln und Protokollierung an die typisierten Clients der
        /// Module LocalLibrary, AppleMusic und TagManager.
        /// </summary>
        /// <remarks>
        /// Muss nach der Registrierung der Module laufen: <c>AddHttpClient&lt;T&gt;()</c> ist
        /// additiv, Basisadresse und Zeitgrenze aus den Modul-Erweiterungen bleiben erhalten.
        /// Die Reihenfolge ist bewusst gewählt — die Wiederholungsregel wird zuerst
        /// angehängt und wirkt damit als äußerer Handler, die Ratenbremse liegt innen.
        /// So wartet jeder Wiederholungsversuch erneut an der Bremse und die Quote der
        /// fremden Schnittstelle bleibt gewahrt.
        /// </remarks>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayTypedHttpClients(this IServiceCollection services)
        {
            AttachResilience(services.AddHttpClient<IAppleMusicSearchClient, AppleMusicSearchClient>(), withRateLimit: true);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverService>(), withRateLimit: false);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverArtArchiveSearchService>(), withRateLimit: true);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.ITunesCoverSearchService>(), withRateLimit: true);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerArtistCoverSearchService>(), withRateLimit: false);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerAlbumCoverSearchService>(), withRateLimit: false);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DiscogsCoverSearchService>(), withRateLimit: true);

            // MusicBrainzLookupService ist internal; der benannte Client heißt per Konvention
            // wie die Schnittstelle des typisierten Clients.
            AttachResilience(services.AddHttpClient("ITagLookupService"), withRateLimit: true);
            return services;
        }

        /// <summary>
        /// Hängt die Standard-Wiederholungsregel, wahlweise die Ratenbremse und in jedem
        /// Fall die Anfrage-Protokollierung an einen Client.
        /// </summary>
        /// <param name="clientBuilder">Der Aufbau des betroffenen Clients.</param>
        /// <param name="withRateLimit">Ob die Ratenbremse je Gegenstelle greifen soll.</param>
        public static void AttachResilience(IHttpClientBuilder clientBuilder, bool withRateLimit)
        {
            _ = clientBuilder.AddStandardResilienceHandler();
            if (withRateLimit)
            {
                _ = clientBuilder.AddHttpMessageHandler<RateLimitMessageHandler>();
            }

            AttachRequestLogging(clientBuilder);
        }

        /// <summary>
        /// Hängt den Protokoll-Handler als innersten Handler an einen Client. Damit
        /// hinterlässt jede einzelne Wiederholung eine eigene Zeile mit Methode, Adresse
        /// (redigiert), Status, Dauer und Client-Namen.
        /// </summary>
        /// <param name="clientBuilder">Der Aufbau des betroffenen Clients.</param>
        public static void AttachRequestLogging(IHttpClientBuilder clientBuilder)
        {
            ArgumentNullException.ThrowIfNull(clientBuilder);

            string clientName = clientBuilder.Name;
            _ = clientBuilder.AddHttpMessageHandler(provider =>
                new HttpRequestLoggingHandler(
                    provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                    clientName));
        }

        /// <summary>Client für den Cover-Abruf: kurze Zeitgrenze, drei Wiederholungen.</summary>
        private static void AddCoverDownloadClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient(CoverDownloader.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-CoverDownload/1.0");
            });

            _ = builder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });

            AttachRequestLogging(builder);
        }

        /// <summary>Client für die Erreichbarkeitsprüfung: sehr kurze Zeitgrenze, keine Wiederholung.</summary>
        private static void AddOnlineCheckClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient("OnlineCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-OnlineCheck/1.0");
            });

            AttachRequestLogging(builder);
        }

        /// <summary>
        /// Client für den Download des Installationspakets. Großzügige Zeitgrenze für die
        /// rund 80 MB des eigenständigen Pakets, auch auf langsamen Leitungen.
        /// </summary>
        /// <remarks>
        /// Bewusst ohne Standard-Wiederholungsregel: Deren Zeitgrenze je Versuch ist für
        /// kleine, gepufferte Aufrufe gedacht und würde einen großen Datenstrom in ein
        /// Versuchsfenster zwängen. Zudem verlangt der eingebaute Schutzschalter eine
        /// Messdauer von mindestens dem doppelten Versuchsfenster — eine Kopplung, die erst
        /// beim ersten Benutzen des Clients zuschlägt und den Download dann vollständig
        /// scheitern lässt. Ein fehlgeschlagener Download wird beim nächsten Start erneut
        /// angeboten.
        /// </remarks>
        private static void AddUpdateDownloadClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient("UpdateDownload", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateDownload/1.0");
            });

            AttachRequestLogging(builder);
        }

        /// <summary>
        /// Client für die Aktualisierungsprüfung gegen GitHub. Zwei Wiederholungen mit
        /// Streuung decken Ratenbegrenzung (429) und kurzzeitige Serverfehler ab, ohne den
        /// Start spürbar aufzuhalten.
        /// </summary>
        private static void AddUpdateCheckClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient("UpdateCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateCheck/1.0");
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            });

            _ = builder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            });

            AttachRequestLogging(builder);
        }
    }
}
