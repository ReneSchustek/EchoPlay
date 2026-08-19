using EchoPlay.App.Composition;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Spotify.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Xunit;

namespace EchoPlay.App.Tests.Composition
{
    /// <summary>
    /// Prüft die Registrierung der Anbieter-Clients: Basisadressen aus der Konfiguration
    /// und die Auflösbarkeit des Spotify-Clients samt Anmeldekette.
    /// </summary>
    /// <remarks>
    /// Die Basisadressen stehen in der Konfiguration, damit sie ohne neue Fassung
    /// umgestellt werden können. Greift der Wert nicht durch, zeigt sich das erst beim
    /// ersten Suchlauf — als Fehler, der nach einem Netzproblem aussieht.
    /// </remarks>
    public sealed class ProviderRegistrationTests
    {
        [Fact]
        public void TokenClient_UsesTheConfiguredAuthAddress()
        {
            ServiceProvider provider = BuildProvider(
                authBaseUrl: "https://auth.example.invalid/", apiBaseUrl: null);

            using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("SpotifyToken");

            Assert.Equal(new Uri("https://auth.example.invalid/"), client.BaseAddress);
        }

        [Fact]
        public void ApiClient_UsesTheConfiguredApiAddress()
        {
            ServiceProvider provider = BuildProvider(
                authBaseUrl: null, apiBaseUrl: "https://api.example.invalid/v1/");

            using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("SpotifyApi");

            Assert.Equal(new Uri("https://api.example.invalid/v1/"), client.BaseAddress);
        }

        [Fact]
        public void WithoutConfiguration_TheOfficialAddressesApply()
        {
            ServiceProvider provider = BuildProvider(authBaseUrl: null, apiBaseUrl: null);
            IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

            using HttpClient token = factory.CreateClient("SpotifyToken");
            using HttpClient api = factory.CreateClient("SpotifyApi");

            // Ohne Eintrag muss die Anwendung ohne Konfigurationsdatei laufen.
            Assert.Equal(new Uri("https://accounts.spotify.com/"), token.BaseAddress);
            Assert.Equal(new Uri("https://api.spotify.com/v1/"), api.BaseAddress);
        }

        [Fact]
        public void SpotifyApiClient_IsResolvableWithItsWholeAuthChain()
        {
            ServiceProvider provider = BuildProvider(authBaseUrl: null, apiBaseUrl: null);
            using IServiceScope scope = provider.CreateScope();

            // Der Client hängt an Token-Beschaffung, Anmelde-Handler und Zugangsspeicher.
            // Fehlt ein Glied, fällt das erst beim ersten Suchlauf auf.
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISpotifyApiClient>());
        }

        private static ServiceProvider BuildProvider(string? authBaseUrl, string? apiBaseUrl)
        {
            Dictionary<string, string?> settings = [];
            if (authBaseUrl is not null)
            {
                settings["Spotify:AuthBaseUrl"] = authBaseUrl;
            }

            if (apiBaseUrl is not null)
            {
                settings["Spotify:ApiBaseUrl"] = apiBaseUrl;
            }

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            ServiceCollection services = new();
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<EchoPlay.Core.Abstractions.Time.IClock>(new FakeClock());
            _ = services.AddSingleton(configuration);
            _ = services.AddEchoPlayProviders(configuration);

            return services.BuildServiceProvider();
        }
    }
}
