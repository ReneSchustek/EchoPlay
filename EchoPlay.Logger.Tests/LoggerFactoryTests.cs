using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Tests.Fakes;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für die <see cref="EchoPlay.Logger.Core.LoggerFactory"/>.
    /// Prüft korrekte Logger-Erstellung sowie Weitergabe von Minimum-Level und Senken.
    /// </summary>
    public sealed class LoggerFactoryTests
    {
        /// <summary>
        /// CreateLogger gibt eine nicht-null ILogger-Instanz zurück.
        /// </summary>
        [Fact]
        public void CreateLogger_GibtNichtNullILoggerZurück()
        {
            EchoPlay.Logger.Core.LoggerFactory factory = new([], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            ILogger logger = factory.CreateLogger("Klasse");

            Assert.NotNull(logger);
        }

        /// <summary>
        /// Zwei Aufrufe von CreateLogger liefern unterschiedliche Instanzen.
        /// </summary>
        [Fact]
        public void CreateLogger_ZweiAufrufe_LiefernVerschiedeneInstanzen()
        {
            EchoPlay.Logger.Core.LoggerFactory factory = new([], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            ILogger logger1 = factory.CreateLogger("Klasse1");
            ILogger logger2 = factory.CreateLogger("Klasse2");

            Assert.NotSame(logger1, logger2);
        }

        /// <summary>
        /// Alle von einer Factory erstellten Logger teilen dieselben Senken.
        /// Beide Logger senden ihre Einträge an dieselbe Senke.
        /// </summary>
        [Fact]
        public void ZweiLogger_TeileGleicheSinks_BeideNachrichtenErfasst()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.LoggerFactory factory = new([sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            ILogger loggerA = factory.CreateLogger("KlasseA");
            ILogger loggerB = factory.CreateLogger("KlasseB");

            loggerA.Info("Meldung von A");
            loggerB.Info("Meldung von B");

            Assert.Equal(2, sink.Entries.Count);
            Assert.Equal("KlasseA", sink.Entries[0].Category);
            Assert.Equal("KlasseB", sink.Entries[1].Category);
        }

        /// <summary>
        /// Das Minimum-Level der Factory wird an alle erstellten Logger weitergegeben.
        /// Nachrichten unterhalb des konfigurierten Minimums werden nicht weitergeleitet.
        /// </summary>
        [Fact]
        public void MinimumLevel_WirdAnErstellteLoggerWeitergegeben()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.LoggerFactory factory = new([sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Error });

            ILogger logger = factory.CreateLogger("Klasse");

            logger.Debug("Wird gefiltert");
            logger.Info("Wird gefiltert");
            logger.Warning("Wird gefiltert");
            logger.Error("Wird weitergeleitet");

            _ = Assert.Single(sink.Entries);
            Assert.Equal(LogLevel.Error, sink.Entries[0].Level);
        }
    }
}
