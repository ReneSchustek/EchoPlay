using EchoPlay.App.Composition;
using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EchoPlay.App.Tests.Composition
{
    /// <summary>
    /// Prüft die reale Verdrahtung der Anwendung: Der Host baut sich auf, und jeder
    /// Dienst findet die Abhängigkeiten, die sein Konstruktor verlangt.
    /// </summary>
    /// <remarks>
    /// Der Anlass ist ein Startabsturz, den erst der Anwender bemerkt hat: Ein Dienst war
    /// nicht registriert, der Container warf beim ersten Auflösen, und das Fenster blieb
    /// weiß. Kein Test konnte das sehen, weil keiner den echten Aufbau anfasste.
    /// <para>
    /// Geprüft wird über <see cref="ServiceProviderOptions.ValidateOnBuild"/>. Der Container
    /// löst dabei die Konstruktorketten auf, ohne eine Instanz zu erzeugen. Genau die
    /// Trennung, die es hier braucht: Die Verdrahtung ist prüfbar, ohne dass ein
    /// WinUI-gebundener Dienst einen Fensterkontext verlangt, den es im Testlauf nicht gibt.
    /// </para>
    /// </remarks>
    public sealed class AppHostFactoryTests
    {
        [Fact]
        public void Create_BuildsTheHost_WithoutThrowing()
        {
            using IHost host = AppHostFactory.Create();

            Assert.NotNull(host.Services);
        }

        [Fact]
        public void BuildServiceProvider_WithValidation_ReportsNoUnresolvableDependency()
        {
            ServiceCollection services = CollectRegistrations();

            // ValidateOnBuild meldet jede Kette, der ein Glied fehlt, gesammelt beim Bauen,
            // nicht erst beim ersten Auflösen zur Laufzeit. ValidateScopes fängt zusätzlich
            // den Fall ab, dass ein Singleton einen kurzlebigen Dienst festhält.
            ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            provider.Dispose();
        }

        [Fact]
        public void PageViewModels_ResolveFromTheContainer()
        {
            // ValidateOnBuild prüft nur die Ketten; die Fabriken der Seiten-ViewModels laufen erst
            // beim Auflösen. Fehlt dort ein Dienst im Bündel, merkt es erst der Seitenwechsel.
            using ServiceProvider provider = CollectRegistrations().BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.ViewModels.SearchViewModel>());
            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.ViewModels.OnlineLibraryViewModel>());
            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.ViewModels.LocalLibraryViewModel>());
            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.ViewModels.DashboardViewModel>());
            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.ViewModels.SettingsViewModel>());
        }

        [Fact]
        public void Replication_ResolvesFromTheContainer()
        {
            // Wie oben: Aufgelöst wird nur, eine Verbindung öffnet erst eine Abfrage.
            using ServiceProvider provider = CollectRegistrations().BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<EchoPlay.Data.Replication.Exchange.DeviceReplicationExchange>());
            Assert.NotNull(provider.GetRequiredService<EchoPlay.App.Services.DeviceReplicationService>());
        }

        [Fact]
        public void DbContext_CarriesTheChangeStampInterceptor()
        {
            // Die Optionen des Kontexts entstehen erst beim Auflösen; ValidateOnBuild sieht sie nicht.
            // Fehlte der Interceptor, bekäme keine Zeile einen Änderungszeitpunkt, und der Abgleich
            // hielte jede Änderung für älter als die des anderen Geräts. Eine Verbindung öffnet der
            // Kontext erst bei der ersten Abfrage; die Datenbank wird hier nicht berührt.
            using ServiceProvider provider = CollectRegistrations().BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();

            CoreOptionsExtension? core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

            Assert.NotNull(core?.Interceptors);
            Assert.Contains(core!.Interceptors!, interceptor => interceptor is ChangeStampInterceptor);
        }

        [Fact]
        public void CollectRegistrations_RegistersTheViewModelsTheShellNavigatesTo()
        {
            ServiceCollection services = CollectRegistrations();
            HashSet<Type> registered = services.Select(descriptor => descriptor.ServiceType).ToHashSet();

            // Ohne diese vier startet die Anwendung zwar, aber die Navigation läuft ins
            // Leere, und der Fehler zeigt sich dann erst beim Klick, nicht beim Start.
            Assert.Contains(typeof(EchoPlay.App.ViewModels.MainWindowViewModel), registered);
            Assert.Contains(typeof(EchoPlay.App.ViewModels.StatusBarViewModel), registered);
            Assert.Contains(typeof(EchoPlay.App.ViewModels.DashboardViewModel), registered);
            Assert.Contains(typeof(EchoPlay.App.ViewModels.SettingsViewModel), registered);
        }

        [Fact]
        public void CollectRegistrations_WindowScopedViewModelsAreSingletons_PageViewModelsAreNot()
        {
            ServiceCollection services = CollectRegistrations();

            // Statusleiste und Hauptfenster gibt es genau einmal; jede Seite bekommt dagegen
            // ein frisches Ansichtsmodell, damit kein Zustand einer verlassenen Seite
            // durchschlägt. Kippt eine dieser Lebensdauern, zeigt die neue Seite alte Daten.
            Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(EchoPlay.App.ViewModels.StatusBarViewModel)));
            Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(EchoPlay.App.ViewModels.MainWindowViewModel)));
            Assert.Equal(ServiceLifetime.Transient, LifetimeOf(services, typeof(EchoPlay.App.ViewModels.DashboardViewModel)));
            Assert.Equal(ServiceLifetime.Transient, LifetimeOf(services, typeof(EchoPlay.App.ViewModels.SettingsViewModel)));
        }

        /// <summary>
        /// Baut dieselbe Dienstsammlung, die auch der Host bekommt, über die echten
        /// Registrierungsmethoden in derselben Reihenfolge. So prüft der Test die reale
        /// Verdrahtung und nicht eine nachgebaute Kopie davon.
        /// </summary>
        private static ServiceCollection CollectRegistrations()
        {
            ServiceCollection services = new();

            _ = services
                .AddEchoPlayLogging()
                .AddEchoPlayHttpClients()
                .AddEchoPlayProviders(LoadConfiguration())
                .AddEchoPlayAppServices()
                .AddEchoPlayViewModels();

            _ = services.AddEchoPlayTypedHttpClients();

            return services;
        }

        /// <summary>
        /// Lädt dieselbe Konfigurationsdatei wie die Anwendung. Sie liegt neben der
        /// Testbinärdatei, weil das App-Projekt sie in sein Ausgabeverzeichnis kopiert.
        /// </summary>
        private static IConfiguration LoadConfiguration()
        {
            return new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
                .Build();
        }

        /// <summary>
        /// Liefert die Lebensdauer der zuletzt gewinnenden Registrierung. Der Container
        /// nimmt bei mehrfacher Registrierung desselben Diensts die letzte; der Test
        /// muss dieselbe nehmen, sonst prüft er eine überschriebene Zusage.
        /// </summary>
        private static ServiceLifetime LifetimeOf(ServiceCollection services, Type serviceType)
        {
            ServiceDescriptor descriptor = services.Last(candidate => candidate.ServiceType == serviceType);
            return descriptor.Lifetime;
        }
    }
}
