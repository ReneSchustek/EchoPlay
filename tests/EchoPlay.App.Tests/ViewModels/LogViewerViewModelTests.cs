using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LogViewerViewModel"/>: Verfügbarkeit, Dateiauswahl und Rückkehr
    /// zur Live-Ansicht. Filter und Live-Takt stehen in <see cref="LogViewerFilterTests"/> und
    /// <see cref="LogViewerLiveTests"/>.
    /// </summary>
    public sealed class LogViewerViewModelTests
    {
        [Fact]
        public void IsLogViewerAvailable_FollowsTheCoordinator()
        {
            // Ohne Speicher-Senke gibt es nichts anzuzeigen, der Bereich blendet sich aus.
            LogViewerViewModel withViewer = Build(out _, logViewerAvailable: true);
            LogViewerViewModel withoutViewer = Build(out _, logViewerAvailable: false);

            Assert.True(withViewer.IsLogViewerAvailable);
            Assert.False(withoutViewer.IsLogViewerAvailable);
        }

        [Fact]
        public async Task LoadLogFilesAsync_FillsFileList()
        {
            LogViewerViewModel viewModel = Build(out FakeLogViewerCoordinator logs);
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("2026-07-27.log", Helpers.TestIds.ReferenceDate, @"C:\Logs\2026-07-27.log"));

            await viewModel.LoadLogFilesAsync();

            Assert.NotEmpty(viewModel.AvailableLogFiles);
        }

        [Fact]
        public async Task LoadLogFilesAsync_KeepsTheUsersChoice()
        {
            // Nach jedem Neuladen wieder auf "Live" zu springen würde die gerade
            // geöffnete Datei unter dem Nutzer wegziehen.
            LogViewerViewModel viewModel = Build(out FakeLogViewerCoordinator logs);
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["erste Zeile", "zweite Zeile"];

            await viewModel.LoadLogFilesAsync();
            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await viewModel.LoadLogFilesAsync();

            Assert.Equal("gestern.log", viewModel.SelectedLogFile?.FileName);
        }

        [Fact]
        public async Task FileChoice_ShowsTheContentOfTheFile()
        {
            LogViewerViewModel viewModel = Build(out FakeLogViewerCoordinator logs);
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["erste Zeile", "zweite Zeile"];
            await viewModel.LoadLogFilesAsync();

            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await Task.Yield();

            Assert.Equal(["erste Zeile", "zweite Zeile"], viewModel.LogEntries);
        }

        [Fact]
        public async Task ReturnToLiveView_ShowsTheBufferAgain()
        {
            LogViewerViewModel viewModel = Build(out FakeLogViewerCoordinator logs);
            logs.AddLiveEntry(new EchoPlay.Logger.Models.LogEntry(
                new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc),
                EchoPlay.Logger.Models.LogLevel.Information,
                "Live-Eintrag",
                "Test",
                []));
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["Datei-Zeile"];
            await viewModel.LoadLogFilesAsync();

            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await Task.Yield();
            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[0];
            await Task.Yield();

            Assert.Contains(viewModel.LogEntries, line => line.Contains("Live-Eintrag", StringComparison.Ordinal));
        }

        [Fact]
        public void Dispose_RunsEvenWithoutRunningTimer()
        {
            LogViewerViewModel viewModel = Build(out _);

            viewModel.Dispose();

            Assert.False(viewModel.IsLiveViewActive);
        }

        private static LogViewerViewModel Build(out FakeLogViewerCoordinator logs, bool logViewerAvailable = true)
        {
            logs = new FakeLogViewerCoordinator { IsLiveViewAvailable = logViewerAvailable };
            return new LogViewerViewModel(logs);
        }
    }
}
