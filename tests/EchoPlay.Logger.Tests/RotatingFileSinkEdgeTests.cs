using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Prüft die Randfälle der dateibasierten Protokollierung: kein anlegbares Verzeichnis,
    /// ein verschwundener Ordner, die Rotation bei voller Datei und das Aufräumen.
    /// </summary>
    /// <remarks>
    /// Die Protokollierung ist Beiwerk. Sie darf den Start der Anwendung nicht verhindern,
    /// wenn das Verzeichnis nicht beschreibbar ist, und sie darf beim Schreiben nicht werfen
    /// — sonst reißt sie genau den Weg ab, den sie dokumentieren soll.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis.
    /// </remarks>
    public sealed class RotatingFileSinkEdgeTests
    {
        [Fact]
        public void Sink_WithADirectoryThatCannotBeCreated_FallsBackWithoutThrowing()
        {
            string fallbackFolder = $"Test-{Path.GetRandomFileName()}";
            string fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EchoPlay", fallbackFolder);

            try
            {
                // Ein Pfad mit Nullzeichen lässt sich nicht anlegen. Die Anwendung muss
                // trotzdem starten — nur eben mit dem Ausweichordner.
                using ProbeSink sink = new("C:\\\0kein-pfad", fallbackFolder);

                Assert.True(Directory.Exists(fallbackPath));
            }
            finally
            {
                if (Directory.Exists(fallbackPath))
                {
                    Directory.Delete(fallbackPath, recursive: true);
                }
            }
        }

        [Fact]
        public async Task Write_WhenTheDirectoryIsGone_DoesNotThrow()
        {
            string folder = CreateTempFolder();
            using ProbeSink sink = new(folder, $"Test-{Path.GetRandomFileName()}");

            Directory.Delete(folder, recursive: true);

            // Ein abgezogenes Laufwerk mitten im Betrieb darf keinen Fehler in den Weg
            // tragen, der gerade protokolliert wird.
            await sink.WriteAsync(Entry("nach dem Verschwinden"));
        }

        [Fact]
        public async Task Write_WhenTheCurrentFileIsFull_ContinuesInTheNextOne()
        {
            string folder = CreateTempFolder();
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await File.WriteAllBytesAsync(
                    Path.Combine(folder, $"{today}.log"), new byte[1024 * 1024 + 1],
                    TestContext.Current.CancellationToken);

                using ProbeSink sink = new(folder, $"Test-{Path.GetRandomFileName()}");
                await sink.WriteAsync(Entry("erste Zeile nach der Rotation"));

                // Ohne Rotation wüchse eine Datei über den Tag hinweg, bis kein Editor sie
                // mehr öffnet — genau dann, wenn man sie am dringendsten braucht.
                Assert.True(File.Exists(Path.Combine(folder, $"{today}_001.log")));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Dispose_CalledTwice_StaysQuiet()
        {
            string folder = CreateTempFolder();
            try
            {
                ProbeSink sink = new(folder, $"Test-{Path.GetRandomFileName()}");

                sink.Dispose();
                sink.Dispose();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Cleanup_WhenTheFilesExceedTheSizeLimit_RemovesTheOldestOnes()
        {
            string folder = CreateTempFolder();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    File.WriteAllBytes(
                        Path.Combine(folder, $"2026-01-0{i + 1}.log"), new byte[600 * 1024]);
                }

                LogCleanupService sut = new(new LoggerOptions
                {
                    LogDirectory = folder,
                    RetentionDays = 0,
                    MaxTotalSizeMb = 1,
                });

                sut.Cleanup();

                // Ohne Obergrenze frisst das Protokoll über Monate die Platte voll. Die
                // ältesten Dateien gehen zuerst — der jüngste Lauf ist der interessante.
                Assert.True(Directory.GetFiles(folder, "*.log").Length < 3);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Cleanup_WithAFileThatIsInUse_KeepsGoing()
        {
            string folder = CreateTempFolder();
            FileStream? held = null;
            try
            {
                string blocked = Path.Combine(folder, "2026-01-01.log");
                File.WriteAllBytes(blocked, new byte[1200 * 1024]);
                held = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.Read);

                LogCleanupService sut = new(new LoggerOptions
                {
                    LogDirectory = folder,
                    RetentionDays = 0,
                    MaxTotalSizeMb = 1,
                });

                // Die Datei, in die gerade geschrieben wird, ist gesperrt. Das Aufräumen
                // darf daran nicht scheitern, sondern muss sie überspringen.
                sut.Cleanup();

                Assert.True(File.Exists(blocked));
            }
            finally
            {
                held?.Dispose();
                Directory.Delete(folder, recursive: true);
            }
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-log-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }

        private static LogEntry Entry(string message) => new(
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            LogLevel.Information,
            message,
            "Test",
            []);

        /// <summary>Ablage mit eigener Endung und eigenem Ausweichordner, damit kein Test die echten Protokolle anfasst.</summary>
        private sealed class ProbeSink : RotatingFileSinkBase
        {
            public ProbeSink(string logDirectory, string fallbackFolder)
                : base(logDirectory, fallbackFolder, ".log", maxFileSizeMb: 1)
            {
            }

            protected override string FormatLine(LogEntry entry) => entry.Message;
        }
    }
}
