using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standardimplementierung von <see cref="IConnectionTestCoordinator"/>.
    /// Nutzt pro Aufruf einen eigenen <see cref="IServiceScope"/>, damit der
    /// Scoped-API-Client (Spotify-Token-Handling) sauber erzeugt und wieder freigegeben wird.
    /// </summary>
    public sealed class ConnectionTestCoordinator : IConnectionTestCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Coordinator mit der DI-Scope-Fabrik.
        /// </summary>
        /// <param name="scopeFactory">Für scoped API-Client-Auflösung.</param>
        /// <param name="localizationService">Liefert die Oberflächentexte des Ergebnisses.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public ConnectionTestCoordinator(IServiceScopeFactory scopeFactory, ILocalizationService localizationService, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _localizationService = localizationService;
            _logger = loggerFactory.CreateLogger("ConnectionTestCoordinator");
        }

        /// <inheritdoc />
        /// <param name="provider">Der Anbieter, dessen Erreichbarkeit geprüft wird.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<ConnectionTestResult> TestAsync(ProviderType provider, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.ConnectionTest);

            if (provider == ProviderType.None)
            {
                // Der Text landet in der Oberfläche (Einstellungen → Verbindung testen),
                // deshalb lokalisiert. Die Ausnahme-Meldungen unten kommen dagegen vom
                // Framework und werden unverändert durchgereicht.
                return new ConnectionTestResult(false, _localizationService.Get("ConnectionTestNoProvider"));
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await ProbeAsync(scope.ServiceProvider, provider, cancellationToken);
                return new ConnectionTestResult(true, null);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Zeitgrenze des Clients erreicht: ein Fehlschlag des Tests. Ein Abbruch des
                // Aufrufers dagegen geht nach oben.
                return new ConnectionTestResult(false, ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                return new ConnectionTestResult(false, ex.Message);
            }
        }

        /// <summary>
        /// Stellt je Anbieter eine Minimalanfrage: Eine Künstlersuche reicht, um Token-Fluss und
        /// Netzwerk zu prüfen. Bei beiden kommt Apple Music zuerst, weil es ohne Zugangsdaten auskommt.
        /// </summary>
        private static async Task ProbeAsync(IServiceProvider services, ProviderType provider, CancellationToken cancellationToken)
        {
            if (provider is ProviderType.AppleMusic or ProviderType.Both)
            {
                // iTunes Search API ist öffentlich, kein Token nötig, aber Netzwerk muss erreichbar sein
                _ = await services.GetRequiredService<IAppleMusicSearchClient>().SearchArtistsAsync("test", 1, cancellationToken);
            }

            if (provider is ProviderType.Spotify or ProviderType.Both)
            {
                _ = await services.GetRequiredService<ISpotifyApiClient>().SearchArtistsAsync("test", 1, cancellationToken);
            }
        }
    }
}
