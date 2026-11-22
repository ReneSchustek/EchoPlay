using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Logger.Models;
using System;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Live-Ansicht des Protokolls: Neue Einträge erscheinen, sobald sie geschrieben
    /// werden, und nur solange die Live-Ansicht an ist.
    /// </summary>
    /// <remarks>
    /// Ohne Warteschlange der Oberfläche baut das Ansichtsmodell direkt auf dem meldenden Thread
    /// neu auf; so lässt sich die Reaktion auf eine Meldung ohne WinUI prüfen.
    /// </remarks>
    public sealed class LogViewerLiveTests
    {
        [Fact]
        public void LiveViewOn_ANewEntry_AppearsWithoutRefreshing()
        {
            LogViewerViewModel sut = Build(out FakeLogViewerCoordinator logs);
            sut.IsLiveViewActive = true;

            logs.WriteLiveEntry(Entry("Scan gestartet"));

            Assert.Contains(sut.LogEntries, line => line.Contains("Scan gestartet", StringComparison.Ordinal));
        }

        [Fact]
        public void LiveViewOff_ANewEntry_LeavesTheViewAlone()
        {
            LogViewerViewModel sut = Build(out FakeLogViewerCoordinator logs);
            sut.IsLiveViewActive = true;
            sut.IsLiveViewActive = false;
            int buildsBefore = logs.BuildCallCount;

            logs.WriteLiveEntry(Entry("Scan gestartet"));

            Assert.Equal(0, logs.LiveSubscriberCount);
            Assert.Equal(buildsBefore, logs.BuildCallCount);
        }

        [Fact]
        public void LiveViewOn_ShowsWhatArrivedBeforeSwitchingOn()
        {
            LogViewerViewModel sut = Build(out FakeLogViewerCoordinator logs);
            logs.AddLiveEntry(Entry("schon da"));

            sut.IsLiveViewActive = true;

            Assert.Contains(sut.LogEntries, line => line.Contains("schon da", StringComparison.Ordinal));
        }

        [Fact]
        public void Dispose_WithLiveViewOn_UnsubscribesFromTheCoordinator()
        {
            LogViewerViewModel sut = Build(out FakeLogViewerCoordinator logs);
            sut.IsLiveViewActive = true;

            sut.Dispose();

            // Der Coordinator lebt als Singleton weiter; ein vergessenes Abmelden hielte die
            // verlassene Einstellungsseite samt Ansichtsmodell im Speicher.
            Assert.Equal(0, logs.LiveSubscriberCount);
        }

        [Fact]
        public void SwitchingOnTwice_SubscribesOnce()
        {
            LogViewerViewModel sut = Build(out FakeLogViewerCoordinator logs);

            sut.IsLiveViewActive = true;
            sut.IsLiveViewActive = true;

            Assert.Equal(1, logs.LiveSubscriberCount);
        }

        private static LogEntry Entry(string message)
            => new(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc), LogLevel.Information, message, "Test", []);

        private static LogViewerViewModel Build(out FakeLogViewerCoordinator logs)
        {
            logs = new FakeLogViewerCoordinator();

            return new LogViewerViewModel(logs);
        }
    }
}
