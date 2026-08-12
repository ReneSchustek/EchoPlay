using EchoPlay.App.ViewModels;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using Microsoft.UI.Xaml;
using System;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Sichert Suche und Stufenfilter der Protokollseite ab — samt der Unterscheidung, ob gar
    /// keine Meldungen vorliegen oder ob nur die Suche nichts findet.
    /// </summary>
    public sealed class LogFilterTests
    {
        /// <summary>
        /// Baut ein ViewModel mit drei Meldungen: ein Fehler, eine Warnung, ein Hinweis.
        /// <para>
        /// <c>Activate</c> ohne Dispatcher lädt den Puffer, ohne auf einen UI-Thread
        /// angewiesen zu sein — live eintreffende Meldungen bleiben dabei bewusst außen vor.
        /// </para>
        /// </summary>
        private static LogViewModel BuildViewModel()
        {
            MemorySink sink = new(capacity: 50);
            Write(sink, LogLevel.Error, "Verbindung verloren", "Datenbank");
            Write(sink, LogLevel.Warning, "Bild nicht gefunden", "Cover");
            Write(sink, LogLevel.Information, "Anwendung bereit", "Start");

            LogViewModel viewModel = new(sink);
            viewModel.Activate(null);
            return viewModel;
        }

        /// <summary>Schreibt eine Meldung in den Puffer und wartet, bis sie dort steht.</summary>
        private static void Write(MemorySink sink, LogLevel level, string message, string category) =>
            sink.WriteAsync(new LogEntry(DateTime.UtcNow, level, message, category, [])).GetAwaiter().GetResult();

        [Fact]
        public void Activate_LoadsBufferedEntriesNewestFirst()
        {
            using LogViewModel viewModel = BuildViewModel();

            Assert.Equal(3, viewModel.LogEntries.Count);
            Assert.Equal("Anwendung bereit", viewModel.LogEntries[0].Message);
        }

        /// <summary>
        /// Die Stufe filtert genau, nicht ab einer Schwelle: Wer Warnungen sucht, will
        /// Warnungen sehen und nicht zusätzlich jeden Fehler.
        /// </summary>
        [Fact]
        public void LevelFilter_KeepsOnlyTheChosenLevel()
        {
            using LogViewModel viewModel = BuildViewModel();

            viewModel.LevelFilter = LogLevel.Warning;

            LogEntryViewModel only = Assert.Single(viewModel.LogEntries);
            Assert.Equal("Bild nicht gefunden", only.Message);
        }

        [Fact]
        public void SearchText_MatchesMessageAndCategory()
        {
            using LogViewModel viewModel = BuildViewModel();

            viewModel.SearchText = "cover";

            Assert.Equal(["Bild nicht gefunden"], viewModel.LogEntries.Select(e => e.Message));
        }

        [Fact]
        public void SearchAndLevel_ApplyTogether()
        {
            using LogViewModel viewModel = BuildViewModel();

            viewModel.SearchText = "Verbindung";
            viewModel.LevelFilter = LogLevel.Warning;

            Assert.Empty(viewModel.LogEntries);
        }

        [Fact]
        public void FilterWithoutMatches_ShowsTheNoResultsHint()
        {
            using LogViewModel viewModel = BuildViewModel();

            viewModel.SearchText = "gibt es nicht";

            Assert.Equal(Visibility.Visible, viewModel.NoResultsVisibility);
            Assert.True(viewModel.HasActiveFilter);
        }

        /// <summary>
        /// Ohne Meldungen bleibt der „Nichts gefunden"-Hinweis aus — dort ist nichts da,
        /// nicht nichts gefunden.
        /// </summary>
        [Fact]
        public void EmptyLog_DoesNotShowTheNoResultsHint()
        {
            using LogViewModel viewModel = new(new MemorySink(capacity: 10));
            viewModel.Activate(null);

            viewModel.SearchText = "irgendwas";

            Assert.Equal(Visibility.Collapsed, viewModel.NoResultsVisibility);
        }

        [Fact]
        public void ResetFiltersCommand_BringsBackEveryMessage()
        {
            using LogViewModel viewModel = BuildViewModel();
            viewModel.SearchText = "Cover";
            viewModel.LevelFilter = LogLevel.Warning;

            viewModel.ResetFiltersCommand.Execute(null);

            Assert.Equal(3, viewModel.LogEntries.Count);
            Assert.False(viewModel.HasActiveFilter);
            Assert.Null(viewModel.LevelFilter);
            Assert.Equal(string.Empty, viewModel.SearchText);
        }

        /// <summary>
        /// Das Leeren der Anzeige nimmt auch den Bestand mit — sonst käme die geleerte Liste
        /// beim nächsten Filterwechsel zurück.
        /// </summary>
        [Fact]
        public void ClearCommand_EmptiesTheListForGood()
        {
            using LogViewModel viewModel = BuildViewModel();

            viewModel.ClearCommand.Execute(null);
            viewModel.SearchText = "Cover";
            viewModel.SearchText = string.Empty;

            Assert.Empty(viewModel.LogEntries);
        }
    }
}
