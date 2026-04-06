using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Formatting;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Sinks;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.Logger.Extensions
{
    /// <summary>
    /// Erweiterungsmethoden für die Registrierung des EchoPlay Loggers im DI-Container.
    /// </summary>
    public static class ServiceCollectionExtensions
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
            LoggerOptions options = new();
            configure?.Invoke(options);

            ILogFormatter formatter = new DefaultLogFormatter();
            List<ILogSink> sinks = [];

            if (options.EnableDebugConsole)
            {
                sinks.Add(new DebugConsoleSink(formatter));
            }

            if (options.EnableFileLogging)
            {
                sinks.Add(new FileSink(options.LogDirectory, formatter, options.MaxFileSizeMb));
            }

            MemorySink? memorySink = null;

            if (options.EnableMemorySink)
            {
                memorySink = new(options.MemorySinkCapacity);
                sinks.Add(memorySink);
            }

            // options-Referenz weitergeben – alle Logger lesen MinimumLevel daraus.
            // So greift UpdateMinimumLevel() im LoggerManager sofort bei allen Loggern.
            LoggerFactory loggerFactory = new(sinks, options);
            LogCleanupService cleanupService = new(options);

            // CA2000: LoggerManager wird als Singleton im DI-Container registriert –
            // die Lebensdauer wird vom Host verwaltet, Dispose erfolgt beim App-Shutdown.
#pragma warning disable CA2000
            LoggerManager loggerManager = new(loggerFactory, cleanupService, options);
#pragma warning restore CA2000

            services.AddSingleton(options);
            services.AddSingleton(cleanupService);
            services.AddSingleton<ILoggerFactory>(loggerFactory);
            services.AddSingleton(loggerManager);

            // MemorySink als Singleton registrieren – null wenn EnableMemorySink = false.
            // SettingsViewModel prüft auf null und blendet den Log-Viewer aus.
            if (memorySink is not null)
            {
                services.AddSingleton(memorySink);
            }

            return services;
        }
    }
}