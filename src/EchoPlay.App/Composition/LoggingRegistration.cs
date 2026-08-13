using EchoPlay.Logger.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert die Protokollierung der Anwendung: den EchoPlay-Logger samt
    /// Ablageort und Aufbewahrung sowie die Dämpfung der rahmenwerkseigenen
    /// HTTP-Protokollzeilen.
    /// </summary>
    internal static class LoggingRegistration
    {
        /// <summary>Verzeichnisname der Protokolldateien unterhalb von LocalApplicationData.</summary>
        private const string LogFolderName = "logs";

        /// <summary>
        /// Registriert den EchoPlay-Logger mit Dateiablage, Aufbewahrung und Speicher-Senke.
        /// Die Aufbewahrung wird beim Start aus den Einstellungen nachgezogen.
        /// </summary>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayLogging(this IServiceCollection services)
        {
            return services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EchoPlay", LogFolderName);
                options.MaxFileSizeMb = 10;
                options.RetentionDays = 30;
                options.MaxTotalSizeMb = 100;
                options.MinimumLevel = EchoPlay.Logger.Models.LogLevel.Debug;
                options.EnableDebugConsole = true;
                options.EnableFileLogging = true;
                options.EnableAutoCleanup = true;
                options.EnableMemorySink = true;
                options.MemorySinkCapacity = 100;
            });
        }

        /// <summary>
        /// Dämpft die Protokollzeilen von HttpClient, Resilience und Polly auf Warnung.
        /// EchoPlay protokolliert jede Anfrage und Antwort selbst über den
        /// <see cref="EchoPlay.App.Services.HttpRequestLoggingHandler"/>; ohne diese Filter
        /// stünde jede Anfrage viermal ohne Mehrwert im Ausgabefenster.
        /// </summary>
        /// <param name="logging">Der Protokoll-Aufbau des Hosts.</param>
        public static void DampenFrameworkHttpLogging(ILoggingBuilder logging)
        {
            _ = logging
                .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
                .AddFilter("Microsoft.Extensions.Http", LogLevel.Warning)
                .AddFilter("Microsoft.Extensions.Http.Resilience", LogLevel.Warning)
                .AddFilter("Polly", LogLevel.Warning);
        }
    }
}
