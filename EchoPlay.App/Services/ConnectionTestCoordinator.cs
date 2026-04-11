using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.Data.Entities.Settings;
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

        /// <summary>
        /// Initialisiert den Coordinator mit der DI-Scope-Fabrik.
        /// </summary>
        /// <param name="scopeFactory">Für scoped API-Client-Auflösung.</param>
        public ConnectionTestCoordinator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc />
        public async Task<ConnectionTestResult> TestAsync(ProviderType provider, CancellationToken cancellationToken = default)
        {
            if (provider == ProviderType.None)
            {
                return new ConnectionTestResult(false, "No provider configured");
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();

                switch (provider)
                {
                    case ProviderType.Spotify:
                        ISpotifyApiClient spotifyClient = scope.ServiceProvider.GetRequiredService<ISpotifyApiClient>();
                        // Minimalanfrage – eine Künstlersuche reicht, um Token-Fluss und Netzwerk zu prüfen
                        await spotifyClient.SearchArtistsAsync("test", 1);
                        break;

                    case ProviderType.AppleMusic:
                        IAppleMusicSearchClient appleClient = scope.ServiceProvider.GetRequiredService<IAppleMusicSearchClient>();
                        // iTunes Search API ist öffentlich – kein Token nötig, aber Netzwerk muss erreichbar sein
                        await appleClient.SearchArtistsAsync("test", 1);
                        break;
                }

                return new ConnectionTestResult(true, null);
            }
            catch (HttpRequestException ex)
            {
                return new ConnectionTestResult(false, ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                return new ConnectionTestResult(false, ex.Message);
            }
            catch (JsonException ex)
            {
                return new ConnectionTestResult(false, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return new ConnectionTestResult(false, ex.Message);
            }
        }
    }
}
