using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Baut den Host der Anwendung zusammen. Die eigentlichen Registrierungen stehen in
    /// den Modul-Erweiterungen daneben; diese Klasse legt nur fest, welche Module in
    /// welcher Reihenfolge zum Zug kommen.
    /// </summary>
    internal static class AppHostFactory
    {
        /// <summary>Dateiname der Konfiguration im Anwendungsverzeichnis.</summary>
        private const string ConfigurationFileName = "appsettings.json";

        /// <summary>
        /// Erstellt den fertig konfigurierten Host.
        /// </summary>
        /// <returns>Der aufgebaute Host samt Dienstsammlung.</returns>
        public static IHost Create()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            AddConfiguration(builder);
            LoggingRegistration.DampenFrameworkHttpLogging(builder.Logging);

            _ = builder.Services
                .AddEchoPlayLogging()
                .AddEchoPlayHttpClients()
                .AddEchoPlayProviders(builder.Configuration)
                .AddEchoPlayAppServices()
                .AddEchoPlayViewModels();

            // Zuletzt: hängt Wiederholungsregeln und Protokollierung an die typisierten
            // Clients der Module. Die müssen dafür bereits registriert sein.
            _ = builder.Services.AddEchoPlayTypedHttpClients();

            return builder.Build();
        }

        /// <summary>
        /// Lädt die Konfiguration. Der Pfad ist bewusst absolut: Ein relativer Dateiname
        /// löst gegen das aktuelle Arbeitsverzeichnis auf, und der Start scheitert dann,
        /// sobald die Anwendung nicht aus ihrem Installationsordner heraus gestartet wird.
        /// </summary>
        private static void AddConfiguration(HostApplicationBuilder builder)
        {
            _ = builder.Configuration.AddJsonFile(
                System.IO.Path.Combine(AppContext.BaseDirectory, ConfigurationFileName),
                optional: false);
        }
    }
}
