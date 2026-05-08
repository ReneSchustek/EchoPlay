using EchoPlay.App.Services;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer <see cref="LogViewerCoordinator"/> — Filterlogik (BuildFilteredLiveEntries)
    /// und IsLiveViewAvailable. Datei-IO-Pfade werden uebersprungen, weil sie LoggerManager
    /// mit echtem Filesystem benoetigen wuerden.
    /// </summary>
    public sealed class LogViewerCoordinatorTests
    {
        private static LoggerManager BuildLoggerManager()
        {
            // Minimaler LoggerManager ohne Dateilogging — Coordinator nutzt nur LogDirectory.
            string tmp = Path.Combine(Path.GetTempPath(), "echoplay-tests-" + Guid.NewGuid().ToString("N"));
            LoggerOptions options = new() { LogDirectory = tmp, EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory factory = new([], options);
            LogCleanupService cleanup = new(options);
            return new LoggerManager(factory, cleanup, options);
        }

        [Fact]
        public void IsLiveViewAvailable_WithMemorySink_IsTrue()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 100);
            LogViewerCoordinator coordinator = new(manager, sink);

            Assert.True(coordinator.IsLiveViewAvailable);
        }

        [Fact]
        public void IsLiveViewAvailable_WithoutMemorySink_IsFalse()
        {
            using LoggerManager manager = BuildLoggerManager();
            LogViewerCoordinator coordinator = new(manager, memorySink: null);

            Assert.False(coordinator.IsLiveViewAvailable);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_FiltersByMinimumLevel()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "DBG", "X", []));
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Error, "ERR", "X", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            IReadOnlyList<string> entries = coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Warning);

            string only = Assert.Single(entries);
            Assert.Contains("ERR", only, StringComparison.Ordinal);
        }
    }
}
