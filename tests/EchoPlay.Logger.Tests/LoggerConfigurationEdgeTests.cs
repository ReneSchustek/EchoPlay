using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.DependencyInjection;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Prüft die Einstellungen der Protokollierung: getrenntes Verzeichnis für die
    /// maschinenlesbare Ablage und die Untergrenze der Aufbewahrung.
    /// </summary>
    /// <remarks>
    /// Die Aufbewahrung kommt aus den Einstellungen des Anwenders. Käme dort eine 0 an
    /// und würde übernommen, löschte der nächste Lauf alle Protokolle — genau die, die
    /// man nach einem Fehler braucht.
    /// </remarks>
    public sealed class LoggerConfigurationEdgeTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void UpdateRetentionDays_WithAValueBelowOne_KeepsTheOldSetting(int days)
        {
            LoggerOptions options = new() { RetentionDays = 30 };
            LoggerManager manager = new(
                new LoggerFactory([], options), new LogCleanupService(options), options);

            manager.UpdateRetentionDays(days);

            Assert.Equal(30, options.RetentionDays);
        }

        [Fact]
        public void UpdateRetentionDays_WithAValidValue_TakesIt()
        {
            LoggerOptions options = new() { RetentionDays = 30 };
            LoggerManager manager = new(
                new LoggerFactory([], options), new LogCleanupService(options), options);

            manager.UpdateRetentionDays(7);

            Assert.Equal(7, options.RetentionDays);
        }

        [Fact]
        public void JsonLogDirectory_WhenSet_IsKept()
        {
            LoggerOptions options = new() { JsonLogDirectory = @"D:\Protokolle\json" };

            // Getrennte Ordner erlauben getrennte Rotation: Die maschinenlesbare Ablage
            // wächst anders als die für Menschen.
            Assert.Equal(@"D:\Protokolle\json", options.JsonLogDirectory);
        }

        [Fact]
        public void AddEchoPlayLogger_WithTheJsonSinkSwitchedOn_RegistersIt()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"echoplay-json-{Path.GetRandomFileName()}");
            try
            {
                ServiceCollection services = new();
                _ = services.AddEchoPlayLogger(o =>
                {
                    o.LogDirectory = folder;
                    o.EnableFileLogging = false;
                    o.EnableJsonSink = true;
                });

                ServiceProvider provider = services.BuildServiceProvider();
                LoggerManager manager = provider.GetRequiredService<LoggerManager>();

                // Der Nachweis führt über die Wirkung: Ist die Ablage verdrahtet, legt sie
                // ihr Verzeichnis an.
                Assert.NotNull(manager);
                Assert.True(Directory.Exists(folder));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
    }
}
