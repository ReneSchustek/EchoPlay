using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Formatting;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Sinks;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.Logger.DependencyInjection
{
    /// <summary>
    /// Erweiterungsmethoden für die Registrierung des EchoPlay Loggers im DI-Container.
    /// </summary>
    public static class LoggerServiceCollectionExtensions
    {
        /// <summary>
        /// Registriert den EchoPlay Logger im DI-Container.
        /// </summary>
        /// <param name="services">Die Service-Collection.</param>
        /// <param name="configure">Optionale Konfiguration der Logger-Optionen.</param>
        /// <returns>Die Service-Collection für Method-Chaining.</returns>
        public static IServiceCollection AddEchoPlayLogger(
            this IServiceCollection services,
            Action<LoggerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            LoggerOptions options = new();
            configure?.Invoke(options);

            List<ILogSink> sinks = CreateSinks(options);
            MemorySink? memorySink = sinks.OfType<MemorySink>().FirstOrDefault();

            // options-Referenz weitergeben, denn alle Logger lesen MinimumLevel daraus.
            // So greift UpdateMinimumLevel() im LoggerManager sofort bei allen Loggern.
            LoggerFactory loggerFactory = new(sinks, options);
            LogCleanupService cleanupService = new(options);

            _ = services.AddSingleton(options);
            _ = services.AddSingleton(cleanupService);
            _ = services.AddSingleton<ILoggerFactory>(loggerFactory);

            // Über eine Fabrik statt als fertige Instanz: Nur was der Container selbst erzeugt,
            // gibt er beim Beenden auch frei, und erst dort räumt der Verwalter alte Protokolle auf.
            _ = services.AddSingleton(_ => new LoggerManager(loggerFactory, cleanupService, options));

            // MemorySink als Singleton registrieren (null, wenn EnableMemorySink = false).
            // SettingsViewModel prüft auf null und blendet den Log-Viewer aus.
            if (memorySink is not null)
            {
                _ = services.AddSingleton(memorySink);
            }

            return services;
        }

        /// <summary>Baut die eingeschalteten Ziele der Protokollierung.</summary>
        private static List<ILogSink> CreateSinks(LoggerOptions options)
        {
            ILogFormatter formatter = new DefaultLogFormatter();
            List<ILogSink> sinks = [];

            if (options.EnableDebugConsole)
            {
                sinks.Add(new DebugConsoleSink(formatter));
            }

            if (options.EnableFileLogging)
            {
                sinks.Add(new FileSink(options.LogDirectory, formatter, options.MaxFileSizeMb, options.TimeProvider));
            }

            if (options.EnableJsonSink)
            {
                string jsonDir = options.JsonLogDirectory ?? options.LogDirectory;
                sinks.Add(new JsonLogSink(jsonDir, options.MaxFileSizeMb, options.TimeProvider));
            }

            if (options.EnableMemorySink)
            {
                sinks.Add(new MemorySink(options.MemorySinkCapacity));
            }

            return sinks;
        }
    }
}
