using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für <see cref="MemorySink"/>.
    /// Prüft den Ringpuffer und das Event-Verhalten für das Live-Protokoll.
    /// </summary>
    public sealed class MemorySinkTests
    {
        private static LogEntry MakeEntry(string message) =>
            new(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), LogLevel.Information, message, "Test", []);

        // ── Ringpuffer ────────────────────────────────────────────────────────────

        [Fact]
        public async Task WriteAsync_FillsBuffer()
        {
            MemorySink sink = new(capacity: 3);

            await sink.WriteAsync(MakeEntry("A"));
            await sink.WriteAsync(MakeEntry("B"));
            await sink.WriteAsync(MakeEntry("C"));

            Assert.Equal(3, sink.GetEntries().Count);
        }

        [Fact]
        public async Task WriteAsync_DroppsOldestWhenFull()
        {
            MemorySink sink = new(capacity: 2);

            await sink.WriteAsync(MakeEntry("A"));
            await sink.WriteAsync(MakeEntry("B"));
            await sink.WriteAsync(MakeEntry("C"));

            // "A" muss rausgefallen sein, neueste Einträge bleiben
            IReadOnlyList<LogEntry> entries = sink.GetEntries();
            Assert.Equal(2, entries.Count);
            Assert.Equal("B", entries[0].Message);
            Assert.Equal("C", entries[1].Message);
        }

        [Fact]
        public async Task GetEntries_ReturnsSnapshot_NotLiveReference()
        {
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(MakeEntry("A"));

            IReadOnlyList<LogEntry> snapshot = sink.GetEntries();
            await sink.WriteAsync(MakeEntry("B"));

            // Snapshot darf nicht nachträglich wachsen
            _ = Assert.Single(snapshot);
        }

        // ── ILiveLogSink – Event ──────────────────────────────────────────────────

        [Fact]
        public async Task WriteAsync_FiresLogEntryAdded()
        {
            MemorySink sink = new();
            LogEntry? received = null;
            sink.LogEntryAdded += entry => received = entry;

            LogEntry written = MakeEntry("live");
            await sink.WriteAsync(written);

            Assert.NotNull(received);
            Assert.Equal("live", received.Message);
        }

        [Fact]
        public async Task WriteAsync_FiresEventForEachEntry()
        {
            MemorySink sink = new();
            int callCount = 0;
            sink.LogEntryAdded += _ => callCount++;

            await sink.WriteAsync(MakeEntry("1"));
            await sink.WriteAsync(MakeEntry("2"));
            await sink.WriteAsync(MakeEntry("3"));

            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task WriteAsync_FiresEventEvenWhenBufferFull()
        {
            // Das Event muss auch dann feuern, wenn der älteste Eintrag verdrängt wird –
            // sonst gehen Live-Updates bei vollem Puffer verloren
            MemorySink sink = new(capacity: 1);
            int callCount = 0;
            sink.LogEntryAdded += _ => callCount++;

            await sink.WriteAsync(MakeEntry("A"));
            await sink.WriteAsync(MakeEntry("B"));

            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task WriteAsync_DoesNotFireEventAfterUnsubscribe()
        {
            MemorySink sink = new();
            int callCount = 0;

            void Handler(LogEntry _) => callCount++;
            sink.LogEntryAdded += Handler;

            await sink.WriteAsync(MakeEntry("first"));

            sink.LogEntryAdded -= Handler;

            await sink.WriteAsync(MakeEntry("second"));

            // Nur der erste Eintrag hat das Event ausgelöst
            Assert.Equal(1, callCount);
        }

        // ── Capacity-Validierung ─────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Ctor_ThrowsOnNonPositiveCapacity(int capacity)
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySink(capacity));
        }

        [Fact]
        public void Ctor_ThrowsWhenCapacityExceedsMax()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySink(MemorySink.MaxCapacity + 1));
        }

        [Fact]
        public void Ctor_AcceptsMaxCapacity()
        {
            MemorySink sink = new(MemorySink.MaxCapacity);
            Assert.Empty(sink.GetEntries());
        }

        [Fact]
        public async Task WriteAsync_EventCarriesCorrectEntry()
        {
            MemorySink sink = new();
            LogEntry? captured = null;
            sink.LogEntryAdded += e => captured = e;

            LogEntry expected = new(
                new DateTime(2026, 3, 23, 14, 0, 0),
                LogLevel.Warning,
                "Test-Nachricht",
                "MeinService",
                ["Scope1"]);

            await sink.WriteAsync(expected);

            Assert.NotNull(captured);
            Assert.Equal(expected.Message, captured.Message);
            Assert.Equal(expected.Level, captured.Level);
            Assert.Equal(expected.Category, captured.Category);
        }
    }
}
