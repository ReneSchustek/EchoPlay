using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Das Beenden der Anwendung in geordneter Folge: Hintergrunddienste anhalten,
    /// die Datenbank für den nächsten Start optimieren und den Host freigeben.
    /// </summary>
    /// <remarks>
    /// Alle Schritte laufen blockierend. Das ist hier unkritisch und sogar nötig: Der
    /// Aufrufer ist die Ereignisbehandlung des Fensterschließens, die nicht asynchron sein
    /// kann, und im Herunterfahren gibt es keinen Synchronisationskontext der Oberfläche
    /// mehr, an dem ein Aufruf hängen bleiben könnte.
    /// </remarks>
    internal static class ShutdownSequence
    {
        /// <summary>Wartezeit, die jeder Hintergrunddienst für seinen laufenden Durchgang bekommt.</summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Führt das Beenden aus. Kein Schritt darf das Schließen des Fensters verhindern.
        /// </summary>
        /// <param name="host">Der Host der Anwendung.</param>
        /// <param name="logger">Protokollkanal, sofern die Protokollierung schon stand.</param>
        public static void Run(IHost host, ILogger? logger)
        {
            ArgumentNullException.ThrowIfNull(host);

            StopBackgroundServices(host, logger);
            OptimizeDatabase(host);
            DisposeHost(host);
        }

        /// <summary>
        /// Hält die Hintergrunddienste an, bevor der Host ihre Instanzen freigibt. Jeder
        /// Dienst bekommt <see cref="StopTimeout"/>, um seinen laufenden Durchgang zu
        /// beenden; danach wird hart abgebrochen — die Überschreitung protokolliert der
        /// Dienst selbst.
        /// </summary>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Cleanup beim Fensterschließen: Das Stoppen der Hintergrund-Services ist optional und darf den Shutdown nicht blockieren, unabhängig davon welche Exception aus einer laufenden Iteration geworfen wird.")]
        private static void StopBackgroundServices(IHost host, ILogger? logger)
        {
            try
            {
                // Blockierend, weil die Ereignisbehandlung des Fensterschließens nicht
                // asynchron sein kann. Unkritisch, weil im Herunterfahren kein
                // Synchronisationskontext der Oberfläche mehr existiert, an dem der
                // Aufruf hängen bleiben könnte.
                host.Services.GetRequiredService<BackgroundCoverService>()
                    .StopAsync(StopTimeout).GetAwaiter().GetResult();

                // Ebenfalls blockierend, und aus demselben Grund unbedenklich.
                host.Services.GetRequiredService<BackgroundProviderIdService>()
                    .StopAsync(StopTimeout).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger?.Warning("Stopp der Hintergrund-Services fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Lässt SQLite seine internen Statistiken anhand der Abfragen dieser Sitzung
        /// auffrischen. Das verbessert den Abfrageplan beim nächsten Start und ist
        /// vollständig optional.
        /// </summary>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Cleanup beim Fensterschließen: SQLite-Optimize ist optional und darf den Shutdown nicht blockieren, unabhängig davon welche Exception aus DbContext oder Maintenance-Service geworfen wird.")]
        private static void OptimizeDatabase(IHost host)
        {
            try
            {
                using IServiceScope scope = host.Services.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                // Blockierend, weil die Ereignisbehandlung des Fensterschließens nicht
                // asynchron sein kann und im Herunterfahren kein Synchronisationskontext
                // der Oberfläche mehr existiert, der deadlocken könnte.
                maintenance.OptimizeAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Die Optimierung darf das Beenden niemals blockieren.
            }
        }

        /// <summary>
        /// Gibt den Host frei. Er entsorgt die Singletons in umgekehrter
        /// Registrierungsreihenfolge, weshalb der Wiedergabedienst vor dem
        /// Protokoll-Verwalter an die Reihe kommt. Ohne diese Freigabe würde dessen
        /// 500-ms-Zeitgeber nach dem Schließen des Fensters weiter auslösen und die
        /// Oberfläche zum Absturz bringen. Der asynchrone Weg sichert nebenbei die
        /// Abspielposition, ohne dass die Dienstlogik dafür blockierend werden müsste.
        /// </summary>
        private static void DisposeHost(IHost host)
        {
            // Die Schnittstelle deklariert nur die synchrone Freigabe; die konkrete
            // Implementierung kann zusätzlich asynchron freigeben.
            if (host is IAsyncDisposable asyncHost)
            {
                // Blockierend, weil die Ereignisbehandlung des Fensterschließens nicht
                // asynchron sein kann und im Herunterfahren kein Synchronisationskontext
                // der Oberfläche mehr existiert, der deadlocken könnte.
                asyncHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
