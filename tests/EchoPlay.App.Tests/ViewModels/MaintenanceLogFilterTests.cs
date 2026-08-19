using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Filter des Protokoll-Betrachters in den Einstellungen: den Suchtext, die
    /// Stufenwahl und die Umrechnung zwischen Auswahlfeld und Protokollstufe.
    /// </summary>
    /// <remarks>
    /// Das Auswahlfeld arbeitet mit einem Index, der Filter mit einer Stufe. Stimmt die
    /// Umrechnung nicht, wählt der Anwender „Fehler" und sieht trotzdem jede Kleinigkeit —
    /// oder umgekehrt gar nichts mehr.
    ///
    /// Eigene Datei statt Ergänzung von <c>MaintenanceSettingsViewModelTests</c>: Die
    /// liegt bereits nahe an der Grenze aus <c>testing.md</c>.
    /// </remarks>
    public sealed class MaintenanceLogFilterTests
    {
        [Theory]
        [InlineData(0, LogLevel.Debug)]
        [InlineData(1, LogLevel.Information)]
        [InlineData(2, LogLevel.Warning)]
        [InlineData(3, LogLevel.Error)]
        public void LogLevelFilterIndex_WhenSet_ChoosesTheMatchingLevel(int index, LogLevel expected)
        {
            MaintenanceSettingsViewModel sut = Build(out _);

            sut.LogLevelFilterIndex = index;

            Assert.Equal(expected, sut.LogMinimumLevel);
        }

        [Theory]
        [InlineData(LogLevel.Debug, 0)]
        [InlineData(LogLevel.Information, 1)]
        [InlineData(LogLevel.Warning, 2)]
        [InlineData(LogLevel.Error, 3)]
        public void LogLevelFilterIndex_FollowsTheChosenLevel(LogLevel level, int expected)
        {
            MaintenanceSettingsViewModel sut = Build(out _);

            sut.LogMinimumLevel = level;

            // Beim Öffnen der Seite muss das Auswahlfeld die gemerkte Stufe anzeigen.
            Assert.Equal(expected, sut.LogLevelFilterIndex);
        }

        [Fact]
        public void LogLevelFilterIndex_WithUnknownIndex_FallsBackToEverything()
        {
            MaintenanceSettingsViewModel sut = Build(out _);

            sut.LogLevelFilterIndex = 99;

            // Lieber zu viel zeigen als den Anwender vor einer leeren Liste sitzen lassen.
            Assert.Equal(LogLevel.Debug, sut.LogMinimumLevel);
        }

        [Fact]
        public void LogSearchText_WhenChanged_RefreshesTheEntries()
        {
            MaintenanceSettingsViewModel sut = Build(out FakeLogViewerCoordinator logs);
            int before = logs.BuildCallCount;

            sut.LogSearchText = "Fehler beim Einlesen";

            Assert.Equal("Fehler beim Einlesen", sut.LogSearchText);
            Assert.True(logs.BuildCallCount > before);
            Assert.Equal("Fehler beim Einlesen", logs.LastSearchText);
        }

        [Fact]
        public void LogMinimumLevel_WhenChanged_RefreshesTheEntries()
        {
            MaintenanceSettingsViewModel sut = Build(out FakeLogViewerCoordinator logs);
            int before = logs.BuildCallCount;

            sut.LogMinimumLevel = LogLevel.Warning;

            Assert.True(logs.BuildCallCount > before);
            Assert.Equal(LogLevel.Warning, logs.LastMinimumLevel);
        }

        [Fact]
        public void LogMinimumLevel_SetToTheSameValue_ChangesNothing()
        {
            MaintenanceSettingsViewModel sut = Build(out FakeLogViewerCoordinator logs);
            sut.LogMinimumLevel = LogLevel.Warning;
            int after = logs.BuildCallCount;

            sut.LogMinimumLevel = LogLevel.Warning;

            Assert.Equal(after, logs.BuildCallCount);
        }

        [Fact]
        public void IsLogViewerAvailable_MirrorsTheCoordinator()
        {
            MaintenanceSettingsViewModel available = Build(out _, logViewerAvailable: true);
            MaintenanceSettingsViewModel unavailable = Build(out _, logViewerAvailable: false);

            // Ohne den Zwischenspeicher aus dem Programmstart gibt es keine Live-Ansicht;
            // die Oberfläche muss das zeigen, statt eine leere Liste anzubieten.
            Assert.True(available.IsLogViewerAvailable);
            Assert.False(unavailable.IsLogViewerAvailable);
        }

        [Fact]
        public void Dispose_WithoutRunningLiveView_StaysQuiet()
        {
            MaintenanceSettingsViewModel sut = Build(out _);

            sut.Dispose();
        }

        [Fact]
        public async Task LoadLogFiles_SelectsTheLiveOptionOnTheFirstRun()
        {
            MaintenanceSettingsViewModel sut = Build(out FakeLogViewerCoordinator logs);
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption(
                "echoplay.log", new System.DateTime(2026, 8, 18, 9, 0, 0, System.DateTimeKind.Local), @"D:\logs\echoplay.log"));

            await sut.LoadLogFilesAsync();

            // Beim ersten Öffnen soll die laufende Sitzung zu sehen sein, nicht eine
            // beliebige Datei von gestern.
            Assert.NotEmpty(sut.AvailableLogFiles);
            Assert.NotNull(sut.SelectedLogFile);
        }

        [Fact]
        public async Task LoadLogFiles_CalledTwice_KeepsTheChosenFile()
        {
            MaintenanceSettingsViewModel sut = Build(out FakeLogViewerCoordinator logs);
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption(
                "echoplay.log", new System.DateTime(2026, 8, 18, 9, 0, 0, System.DateTimeKind.Local), @"D:\logs\echoplay.log"));

            await sut.LoadLogFilesAsync();
            EchoPlay.App.Models.LogFileOption? first = sut.SelectedLogFile;

            await sut.LoadLogFilesAsync();

            // Ein Neuladen der Liste darf die Auswahl des Anwenders nicht zurücksetzen.
            Assert.Same(first, sut.SelectedLogFile);
        }

        private static MaintenanceSettingsViewModel Build(
            out FakeLogViewerCoordinator logs, bool logViewerAvailable = true)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IDatabaseMaintenanceService>(_ => new FakeDatabaseMaintenanceService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            logs = new FakeLogViewerCoordinator { IsLiveViewAvailable = logViewerAvailable };

            return new MaintenanceSettingsViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                logs,
                onUserEdit: () => { });
        }
    }
}
