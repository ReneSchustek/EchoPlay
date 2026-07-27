using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.DependencyInjection;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace EchoPlay.Logger.Tests.DependencyInjection
{
    /// <summary>
    /// Prüft die DI-Registrierung des Loggers. Sie entscheidet, welche Senken überhaupt
    /// existieren – ist hier etwas falsch verdrahtet, protokolliert die Anwendung still nichts,
    /// und das fällt erst auf, wenn man ein Protokoll braucht.
    /// </summary>
    public sealed class LoggerServiceCollectionExtensionsTests : IDisposable
    {
        private readonly string _logDirectory;

        /// <summary>
        /// Eigenes Verzeichnis je Testlauf: die Datei-Senke legt beim Erzeugen wirklich
        /// eine Datei an, und xUnit lässt Testklassen parallel laufen.
        /// </summary>
        public LoggerServiceCollectionExtensionsTests()
        {
            _logDirectory = Path.Combine(
                Path.GetTempPath(),
                "EchoPlayLoggerDiTests_" + Guid.NewGuid().ToString("N"));

            _ = Directory.CreateDirectory(_logDirectory);
        }

        [Fact]
        public void AddEchoPlayLogger_WithoutServices_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => LoggerServiceCollectionExtensions.AddEchoPlayLogger(null!));
        }

        [Fact]
        public void AddEchoPlayLogger_Default_RegistersFactoryOptionsAndManager()
        {
            ServiceCollection services = new();

            _ = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = false;
                options.EnableJsonSink = false;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetService<ILoggerFactory>());
            Assert.NotNull(provider.GetService<LoggerOptions>());
            Assert.NotNull(provider.GetService<LoggerManager>());
            Assert.NotNull(provider.GetService<LogCleanupService>());
        }

        [Fact]
        public void AddEchoPlayLogger_ReturnsSameCollection_ForChaining()
        {
            ServiceCollection services = new();

            IServiceCollection returned = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = false;
            });

            Assert.Same(services, returned);
        }

        [Fact]
        public void AddEchoPlayLogger_WithMemorySink_RegistersMemorySink()
        {
            ServiceCollection services = new();

            _ = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = false;
                options.EnableMemorySink = true;
                options.MemorySinkCapacity = 25;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetService<MemorySink>());
        }

        [Fact]
        public void AddEchoPlayLogger_WithoutMemorySink_DoesNotRegisterMemorySink()
        {
            // Der Log-Viewer in den Einstellungen prüft genau darauf und blendet sich sonst aus.
            ServiceCollection services = new();

            _ = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = false;
                options.EnableMemorySink = false;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Null(provider.GetService<MemorySink>());
        }

        [Fact]
        public void AddEchoPlayLogger_WithFileLogging_WritesToConfiguredDirectory()
        {
            ServiceCollection services = new();

            _ = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = true;
                options.EnableJsonSink = false;
                options.MinimumLevel = LogLevel.Information;
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();

            factory.CreateLogger("Test").Info("Eintrag aus dem DI-Test.");

            Assert.NotEmpty(Directory.GetFiles(_logDirectory, "*.log"));
        }

        [Fact]
        public void AddEchoPlayLogger_PassesOptionsInstanceToFactory()
        {
            // Dieselbe Options-Instanz muss registriert sein: LoggerManager.UpdateMinimumLevel
            // ändert sie zur Laufzeit, und alle Logger lesen den Wert von dort.
            ServiceCollection services = new();

            _ = services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = _logDirectory;
                options.EnableFileLogging = false;
                options.MinimumLevel = LogLevel.Warning;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Equal(LogLevel.Warning, provider.GetRequiredService<LoggerOptions>().MinimumLevel);
        }

        /// <summary>Räumt das Log-Verzeichnis des Tests wieder ab.</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    Directory.Delete(_logDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Aufräumen ist Kür: hält die Datei-Senke die Datei noch, bleibt das
                // Temp-Verzeichnis liegen — der Test soll deshalb nicht rot werden.
            }
        }
    }
}
