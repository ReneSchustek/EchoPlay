using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="EchoPlay.Logger.Core.LoggerManager"/>.
    /// Prüft Factory-Property und korrektes Dispose-Verhalten.
    /// </summary>
    public sealed class LoggerManagerTests
    {
        /// <summary>
        /// Die Factory-Property gibt dieselbe Instanz zurück, die im Konstruktor übergeben wurde.
        /// </summary>
        [Fact]
        public void Factory_GibtRegistrierteFactory_Zurück()
        {
            EchoPlay.Logger.Core.LoggerFactory loggerFactory = new([], new LoggerOptions { MinimumLevel = LogLevel.Debug });
            LogCleanupService cleanupService = new(new LoggerOptions());
            EchoPlay.Logger.Core.LoggerManager manager = new(loggerFactory, cleanupService, new LoggerOptions());

            Assert.Same(loggerFactory, manager.Factory);
        }

        /// <summary>
        /// Dispose kann mehrfach aufgerufen werden ohne eine Exception zu werfen (Idempotenz).
        /// </summary>
        [Fact]
        public void Dispose_KannMehrfachAufgerufenWerden_OhneException()
        {
            EchoPlay.Logger.Core.LoggerFactory loggerFactory = new([], new LoggerOptions { MinimumLevel = LogLevel.Debug });
            LogCleanupService cleanupService = new(new LoggerOptions());
            EchoPlay.Logger.Core.LoggerManager manager = new(loggerFactory, cleanupService, new LoggerOptions());

            manager.Dispose();
            manager.Dispose();
            manager.Dispose();
        }

        /// <summary>
        /// Nach Dispose ist die Factory-Property weiterhin zugänglich.
        /// </summary>
        [Fact]
        public void Factory_NachDispose_WeiterhinZugänglich()
        {
            EchoPlay.Logger.Core.LoggerFactory loggerFactory = new([], new LoggerOptions { MinimumLevel = LogLevel.Debug });
            LogCleanupService cleanupService = new(new LoggerOptions());
            EchoPlay.Logger.Core.LoggerManager manager = new(loggerFactory, cleanupService, new LoggerOptions());

            manager.Dispose();

            // Factory-Property bleibt verfügbar
            Assert.Same(loggerFactory, manager.Factory);
        }
    }
}
