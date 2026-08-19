using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Dateiwege des Protokoll-Betrachters: welche Dateien er anbietet, in
    /// welcher Reihenfolge und was er aus einer gewählten Datei liest.
    /// </summary>
    /// <remarks>
    /// Die Live-Ansicht zeigt nur, was seit dem Programmstart passiert ist. Wer einem
    /// Fehler von gestern nachgeht, ist auf die Dateien angewiesen — steht die falsche
    /// oben oder fehlt eine, sucht er im leeren Protokoll.
    ///
    /// Die Tests legen echte Dateien im Temp-Verzeichnis an und räumen sie wieder ab; der
    /// geprüfte Weg liest ein Verzeichnis und ist anders nicht zu erreichen.
    /// </remarks>
    public sealed class LogViewerFileTests
    {
        [Fact]
        public async Task LoadLogFileOptions_PutsTheLiveOptionFirst()
        {
            string dir = CreateLogDirectory();
            try
            {
                WriteLog(dir, "echoplay-20260818.log", new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Local));

                LogViewerCoordinator sut = Build(dir);

                IReadOnlyList<LogFileOption> options = await sut.LoadLogFileOptionsAsync(
                    TestContext.Current.CancellationToken);

                // Die Live-Ansicht ist die Vorgabe; sie muss ohne Suchen erreichbar sein.
                Assert.Equal(2, options.Count);
                Assert.Null(options[0].FilePath);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoadLogFileOptions_ListsTheNewestFileFirst()
        {
            string dir = CreateLogDirectory();
            try
            {
                WriteLog(dir, "echoplay-alt.log", new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Local));
                WriteLog(dir, "echoplay-neu.log", new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Local));

                LogViewerCoordinator sut = Build(dir);

                IReadOnlyList<LogFileOption> options = await sut.LoadLogFileOptionsAsync(
                    TestContext.Current.CancellationToken);

                // Wer ins Protokoll schaut, sucht fast immer den letzten Lauf.
                Assert.Equal("echoplay-neu.log", options[1].FileName);
                Assert.Equal("echoplay-alt.log", options[2].FileName);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoadLogFileOptions_IgnoresFilesThatAreNoLogs()
        {
            string dir = CreateLogDirectory();
            try
            {
                WriteLog(dir, "echoplay.log", new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Local));
                await File.WriteAllTextAsync(
                    Path.Combine(dir, "hinweis.txt"), "kein Protokoll", TestContext.Current.CancellationToken);

                LogViewerCoordinator sut = Build(dir);

                IReadOnlyList<LogFileOption> options = await sut.LoadLogFileOptionsAsync(
                    TestContext.Current.CancellationToken);

                Assert.Equal(2, options.Count);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoadFileLines_ReturnsEveryLineOfTheFile()
        {
            string dir = CreateLogDirectory();
            try
            {
                string path = Path.Combine(dir, "echoplay.log");
                await File.WriteAllLinesAsync(
                    path, ["erste Zeile", "zweite Zeile"], TestContext.Current.CancellationToken);

                LogViewerCoordinator sut = Build(dir);

                IReadOnlyList<string> lines = await sut.LoadFileLinesAsync(
                    path, TestContext.Current.CancellationToken);

                Assert.Equal(["erste Zeile", "zweite Zeile"], lines);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoadFileLines_ForAMissingFile_ReturnsNothing()
        {
            string dir = CreateLogDirectory();
            try
            {
                LogViewerCoordinator sut = Build(dir);

                // Die Datei kann zwischen dem Aufklappen der Liste und der Auswahl
                // rotiert worden sein. Der Betrachter darf daran nicht reißen.
                IReadOnlyList<string> lines = await sut.LoadFileLinesAsync(
                    Path.Combine(dir, "gibt-es-nicht.log"), TestContext.Current.CancellationToken);

                Assert.Empty(lines);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static LogViewerCoordinator Build(string logDirectory)
        {
            LoggerOptions options = new()
            {
                LogDirectory = logDirectory,
                EnableFileLogging = false,
                EnableAutoCleanup = false,
            };
            LoggerFactory factory = new([], options);
            LogCleanupService cleanup = new(options);
            return new LogViewerCoordinator(new LoggerManager(factory, cleanup, options), memorySink: null);
        }

        private static string CreateLogDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-logs-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteLog(string directory, string fileName, DateTime lastWrite)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "Beispielzeile");
            File.SetLastWriteTime(path, lastWrite);
        }
    }
}
