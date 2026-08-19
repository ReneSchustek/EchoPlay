using EchoPlay.App.Startup;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Startup
{
    /// <summary>
    /// Prüft das geordnete Beenden der Anwendung.
    /// </summary>
    /// <remarks>
    /// Der Ablauf hängt am Schließen des Fensters. Bleibt er an einem Schritt hängen oder
    /// wirft er, steht das Fenster offen und der Anwender kann die Anwendung nur noch über
    /// den Task-Manager beenden. Deshalb prüfen die Tests vor allem, dass jeder Schritt
    /// scheitern darf, ohne die folgenden mitzunehmen.
    /// </remarks>
    public sealed class ShutdownSequenceTests
    {
        [Fact]
        public void Run_WithoutBackgroundServices_StillOptimizesAndReleasesTheHost()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            SyncHost host = new(BuildProvider(maintenance));

            ShutdownSequence.Run(host, new FakeLoggerFactory().CreateLogger("Test"));

            // Die Hintergrunddienste fehlen — trotzdem laufen Optimierung und Freigabe.
            Assert.Equal(1, maintenance.OptimizeCount);
            Assert.True(host.WasDisposed);
        }

        [Fact]
        public void Run_WithoutALogger_StillReleasesTheHost()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            SyncHost host = new(BuildProvider(maintenance));

            // Wird das Fenster geschlossen, bevor die Protokollierung stand, gibt es keinen
            // Kanal für die Warnung. Das darf das Beenden nicht aufhalten.
            ShutdownSequence.Run(host, logger: null);

            Assert.True(host.WasDisposed);
        }

        [Fact]
        public void Run_WhenTheOptimizationFails_StillReleasesTheHost()
        {
            FakeDatabaseMaintenanceService maintenance = new() { FailureMessage = "Datenbank gesperrt" };
            SyncHost host = new(BuildProvider(maintenance));

            ShutdownSequence.Run(host, new FakeLoggerFactory().CreateLogger("Test"));

            Assert.Equal(1, maintenance.OptimizeCount);
            Assert.True(host.WasDisposed);
        }

        [Fact]
        public void Run_WithoutAMaintenanceService_StillReleasesTheHost()
        {
            SyncHost host = new(new ServiceCollection().BuildServiceProvider());

            // Die Optimierung ist reine Vorsorge für den nächsten Start. Fehlt der Dienst,
            // wird sie übersprungen — das Fenster schließt trotzdem.
            ShutdownSequence.Run(host, new FakeLoggerFactory().CreateLogger("Test"));

            Assert.True(host.WasDisposed);
        }

        [Fact]
        public void Run_WithAnAsyncHost_ReleasesItAsynchronously()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            AsyncHost host = new(BuildProvider(maintenance));

            ShutdownSequence.Run(host, new FakeLoggerFactory().CreateLogger("Test"));

            // Der Wiedergabedienst sichert beim Freigeben noch die Abspielposition. Kann
            // der Host asynchron freigeben, muss dieser Weg genommen werden — sonst geht
            // die zuletzt gehörte Stelle verloren.
            Assert.True(host.WasDisposedAsync);
            Assert.False(host.WasDisposed);
        }

        [Fact]
        public void Run_WithoutAHost_Throws()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => ShutdownSequence.Run(host: null!, logger: null));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static ServiceProvider BuildProvider(FakeDatabaseMaintenanceService maintenance)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IDatabaseMaintenanceService>(_ => maintenance);
            return services.BuildServiceProvider();
        }

        /// <summary>Host, der nur synchron freigeben kann.</summary>
        private class SyncHost : IHost
        {
            private readonly ServiceProvider _provider;

            public SyncHost(ServiceProvider provider) => _provider = provider;

            public IServiceProvider Services => _provider;

            public bool WasDisposed { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public void Dispose()
            {
                WasDisposed = true;
                _provider.Dispose();
            }
        }

        /// <summary>Host, der zusätzlich asynchron freigeben kann.</summary>
        private sealed class AsyncHost : SyncHost, IAsyncDisposable
        {
            public AsyncHost(ServiceProvider provider) : base(provider)
            {
            }

            public bool WasDisposedAsync { get; private set; }

            public ValueTask DisposeAsync()
            {
                WasDisposedAsync = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
