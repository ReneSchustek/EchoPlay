using EchoPlay.App.Services;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert alle HTTP-Clients der Anwendung an einer Stelle. Damit gelten
    /// einheitliche Zeitgrenzen, Kennungen und Wiederholungsregeln, statt dass jeder
    /// Verbraucher eine eigene Instanz hält.
    /// </summary>
    /// <remarks>
    /// An einem Client mit Wiederholungsregel steht die Zeitgrenze ausschließlich in deren
    /// Optionen (<c>AttemptTimeout</c>, <c>TotalRequestTimeout</c>). Die Regel setzt
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> auf unendlich, sobald sie nach der
    /// Client-Konfiguration angehängt wird. Eine dort gesetzte Grenze sähe aus, als gelte sie,
    /// und hätte keine Wirkung.
    /// </remarks>
    internal static class HttpClientRegistration
    {
        /// <summary>
        /// Grenze je Versuch an den Clients der fremden Schnittstellen. Eine Suchanfrage
        /// antwortet im Normalfall in unter einer Sekunde; zehn Sekunden lassen Raum für
        /// eine langsame Leitung und für die Wartezeit an der Ratenbremse, die innerhalb
        /// des Versuchs liegt.
        /// </summary>
        internal static readonly TimeSpan ProviderAttemptTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gesamtgrenze einer Anfrage an eine fremde Schnittstelle, alle Wiederholungen
        /// eingerechnet. Kürzer schnitte sie die Wartezeit ab, die ein 429 mit
        /// <c>Retry-After</c> verlangt, und der Aufruf scheiterte, obwohl der nächste
        /// Versuch durchgegangen wäre.
        /// </summary>
        internal static readonly TimeSpan ProviderTotalTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Grenze je Versuch der Aktualisierungsprüfung. GitHub beantwortet die Abfrage
        /// des neuesten Release in der Regel in einer Viertelsekunde; zwei Sekunden lassen
        /// Raum für eine langsame Leitung und noch einen zweiten Versuch innerhalb der
        /// Gesamtgrenze.
        /// </summary>
        internal static readonly TimeSpan UpdateCheckAttemptTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gesamtgrenze der Aktualisierungsprüfung samt Wiederholungen. Die Prüfung läuft im
        /// Startbild und hält den Start auf, solange sie dauert; länger als fünf Sekunden
        /// soll niemand auf das Fenster warten, nur weil GitHub nicht antwortet.
        /// </summary>
        internal static readonly TimeSpan UpdateCheckTotalTimeout = TimeSpan.FromSeconds(5);

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
        /// additiv, die Basisadresse aus den Modul-Erweiterungen bleibt erhalten.
        /// Die Reihenfolge ist gewollt: Die Wiederholungsregel wird zuerst
        /// angehängt und wirkt damit als äußerer Handler, die Ratenbremse liegt innen.
        /// So wartet jeder Wiederholungsversuch erneut an der Bremse und die Quote der
        /// fremden Schnittstelle bleibt gewahrt.
        /// </remarks>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayTypedHttpClients(this IServiceCollection services)
        {
            AttachResilience(services.AddHttpClient<IAppleMusicSearchClient, AppleMusicSearchClient>(), HostThrottling.PerHost);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverService>(), HostThrottling.None);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverArtArchiveSearchService>(), HostThrottling.PerHost);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.ITunesCoverSearchService>(), HostThrottling.PerHost);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerArtistCoverSearchService>(), HostThrottling.None);
            AttachResilience(services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerAlbumCoverSearchService>(), HostThrottling.None);

            // MusicBrainzLookupService ist internal; der benannte Client heißt per Konvention
            // wie die Schnittstelle des typisierten Clients.
            AttachResilience(services.AddHttpClient("ITagLookupService"), HostThrottling.PerHost);
            return services;
        }

        /// <summary>
        /// Hängt die Standard-Wiederholungsregel, je nach <paramref name="throttling"/> die
        /// Ratenbremse und in jedem Fall die Anfrage-Protokollierung an einen Client.
        /// </summary>
        /// <param name="clientBuilder">Der Aufbau des betroffenen Clients.</param>
        /// <param name="throttling">Ob die Ratenbremse je Gegenstelle greifen soll.</param>
        /// <param name="configureOptions">
        /// Eigene Optionen der Wiederholungsregel; ohne Angabe gelten die Grenzen der
        /// fremden Schnittstellen (<see cref="ProviderAttemptTimeout"/>,
        /// <see cref="ProviderTotalTimeout"/>).
        /// </param>
        public static void AttachResilience(
            IHttpClientBuilder clientBuilder,
            HostThrottling throttling,
            Action<HttpStandardResilienceOptions>? configureOptions = null)
        {
            AttachRetryPipeline(clientBuilder, configureOptions);
            if (throttling == HostThrottling.PerHost)
            {
                _ = clientBuilder.AddHttpMessageHandler<RateLimitMessageHandler>();
            }

            AttachRequestLogging(clientBuilder);
        }

        /// <summary>
        /// Hängt nur die Standard-Wiederholungsregel an einen Client. Für Clients, die
        /// zwischen Regel und Protokollierung einen eigenen Handler brauchen.
        /// </summary>
        /// <param name="clientBuilder">Der Aufbau des betroffenen Clients.</param>
        /// <param name="configureOptions">
        /// Eigene Optionen der Wiederholungsregel; ohne Angabe gelten die Grenzen der
        /// fremden Schnittstellen.
        /// </param>
        public static void AttachRetryPipeline(
            IHttpClientBuilder clientBuilder,
            Action<HttpStandardResilienceOptions>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(clientBuilder);

            _ = clientBuilder.AddStandardResilienceHandler(configureOptions ?? ApplyProviderTimeouts);
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

        /// <summary>
        /// Setzt die Grenzen der fremden Schnittstellen. Der Standard der Bibliothek hat
        /// heute dieselben Werte; sie stehen trotzdem hier, damit die geltende Grenze im
        /// Code steht und nicht an einer Paketversion hängt.
        /// </summary>
        private static void ApplyProviderTimeouts(HttpStandardResilienceOptions options)
        {
            options.AttemptTimeout.Timeout = ProviderAttemptTimeout;
            options.TotalRequestTimeout.Timeout = ProviderTotalTimeout;
        }

        /// <summary>
        /// Client für den Cover-Abruf mit den Grenzen der fremden Schnittstellen und der
        /// Standard-Wiederholung (drei Versuche, wachsender Abstand mit Streuung).
        /// </summary>
        private static void AddCoverDownloadClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient(CoverDownloader.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-CoverDownload/1.0");
            });

            AttachRetryPipeline(builder);
            AttachRequestLogging(builder);
        }

        /// <summary>
        /// Client für die Erreichbarkeitsprüfung: fünf Sekunden, keine Wiederholung. Die Prüfung
        /// läuft im Startbild; wer offline ist, soll nicht länger auf das Fenster warten.
        /// </summary>
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
        /// Client für den Download des Installationspakets. Fünf Minuten für die rund 80 MB
        /// des eigenständigen Pakets reichen bis hinunter zu etwa 270 KB je Sekunde.
        /// </summary>
        /// <remarks>
        /// Ohne Standard-Wiederholungsregel: Deren Zeitgrenze je Versuch ist für kleine,
        /// gepufferte Aufrufe gedacht und würde einen großen Datenstrom in ein Versuchsfenster
        /// zwängen. Zudem verlangt der eingebaute Schutzschalter eine Messdauer von mindestens
        /// dem doppelten Versuchsfenster, eine Kopplung, die erst beim ersten Benutzen des
        /// Clients zuschlägt und den Download dann vollständig scheitern lässt. Ein
        /// fehlgeschlagener Download wird beim nächsten Start erneut angeboten.
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
        /// Client für die Aktualisierungsprüfung gegen GitHub. Diese Regel ist die einzige
        /// Frist der Prüfung (<see cref="UpdateCheckAttemptTimeout"/>,
        /// <see cref="UpdateCheckTotalTimeout"/>); der Dienst setzt keine eigene.
        /// </summary>
        /// <remarks>
        /// Die Wiederholungen fangen einen hängenden ersten Versuch und kurze Serverfehler ab.
        /// Gegen die Ratenbegrenzung von GitHub helfen sie nicht: Ein 429 verlangt dort
        /// Minuten, die Gesamtgrenze bricht vorher ab, und das Update wird beim nächsten
        /// Start erneut gesucht.
        /// </remarks>
        private static void AddUpdateCheckClient(IServiceCollection services)
        {
            IHttpClientBuilder builder = services.AddHttpClient("UpdateCheck", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateCheck/1.0");
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            });

            _ = builder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;

                // Der Standard wartet zwei Sekunden vor der ersten Wiederholung; das ließe in
                // fünf Sekunden keinen vollständigen zweiten Versuch mehr zu.
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.AttemptTimeout.Timeout = UpdateCheckAttemptTimeout;
                options.TotalRequestTimeout.Timeout = UpdateCheckTotalTimeout;
            });

            AttachRequestLogging(builder);
        }
    }
}
