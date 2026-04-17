using System.Diagnostics.CodeAnalysis;
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
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "LoggerManager wird als Singleton im DI-Container registriert; der Host verwaltet die Lebensdauer bis App-Shutdown.")]
        public static IServiceCollection AddEchoPlayLogger(
            this IServiceCollection services,
            Action<LoggerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

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

            LoggerManager loggerManager = new(loggerFactory, cleanupService, options);

            _ = services.AddSingleton(options);
            _ = services.AddSingleton(cleanupService);
            _ = services.AddSingleton<ILoggerFactory>(loggerFactory);
            _ = services.AddSingleton(loggerManager);

            // MemorySink als Singleton registrieren – null wenn EnableMemorySink = false.
            // SettingsViewModel prüft auf null und blendet den Log-Viewer aus.
            if (memorySink is not null)
            {
                _ = services.AddSingleton(memorySink);
            }

            return services;
        }
    }
}
