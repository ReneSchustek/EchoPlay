using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Replication;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="HybridTimestamp"/> und <see cref="HybridLogicalClock"/>.
    /// </summary>
    public sealed class HybridClockTests
    {
        private static readonly Guid DeviceA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid DeviceB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        [Fact]
        public void Timestamp_TextRoundTrip_KeepsAllParts()
        {
            HybridTimestamp stamp = new(1_763_640_000_123, 42, DeviceA);

            Assert.True(HybridTimestamp.TryParse(stamp.ToString(), out HybridTimestamp parsed));
            Assert.Equal(stamp, parsed);
            Assert.Equal(HybridTimestamp.TextLength, stamp.ToString().Length);
        }

        [Fact]
        public void Timestamp_OrdinalTextOrder_MatchesCompareTo()
        {
            // Die Textform muss sich sortieren lassen wie die Werte; sonst sortierten Datenbank und
            // Dateien anders als der Code.
            HybridTimestamp[] stamps =
            [
                new(9, 0, DeviceA), new(10, 0, DeviceA), new(10, 1, DeviceA), new(10, 1, DeviceB),
                new(100, 0, DeviceA), new(1_763_640_000_000, 999, DeviceB), new(0, 5, DeviceB),
            ];

            string[] byValue = [.. stamps.Order().Select(s => s.ToString())];
            string[] byText = [.. stamps.Select(s => s.ToString()).Order(StringComparer.Ordinal)];

            Assert.Equal(byValue, byText);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("001763640000000-000000001-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("00176364000000x.000000001.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("-01763640000000.000000001.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("001763640000000.000000001.zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
        [InlineData("001763640000000.000000001.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void Timestamp_TryParse_RejectsAnythingButTheExactForm(string? text)
        {
            Assert.False(HybridTimestamp.TryParse(text, out _));
        }

        [Fact]
        public void Now_WithinSameMillisecond_CountsUp()
        {
            SteppingClock wall = new();
            HybridLogicalClock clock = new(wall, new FixedDevice(DeviceA));

            HybridTimestamp first = clock.Now();
            HybridTimestamp second = clock.Now();

            Assert.Equal(first.WallMilliseconds, second.WallMilliseconds);
            Assert.Equal(first.Counter + 1, second.Counter);
            Assert.True(second > first);
        }

        [Fact]
        public void Now_WhenWallClockGoesBack_StillIncreases()
        {
            SteppingClock wall = new();
            HybridLogicalClock clock = new(wall, new FixedDevice(DeviceA));
            HybridTimestamp before = clock.Now();

            wall.Advance(TimeSpan.FromMinutes(-5));
            HybridTimestamp after = clock.Now();

            Assert.True(after > before);
            Assert.Equal(before.WallMilliseconds, after.WallMilliseconds);
        }

        [Fact]
        public void Observe_RemoteFromTheFuture_PullsClockForward()
        {
            SteppingClock wall = new();
            HybridLogicalClock clock = new(wall, new FixedDevice(DeviceA));
            HybridTimestamp remote = new(clock.Now().WallMilliseconds + 60_000, 7, DeviceB);

            HybridTimestamp observed = clock.Observe(remote);
            HybridTimestamp next = clock.Now();

            Assert.True(observed > remote);
            Assert.True(next > observed);
            Assert.Equal(DeviceA, next.DeviceId);
        }

        [Fact]
        public void Now_FromManyThreads_GivesNoDuplicate()
        {
            HybridLogicalClock clock = new(new SteppingClock(), new FixedDevice(DeviceA));
            System.Collections.Concurrent.ConcurrentBag<HybridTimestamp> stamps = [];

            _ = Parallel.For(0, 10_000, _ => stamps.Add(clock.Now()));

            Assert.Equal(10_000, stamps.Distinct().Count());
        }

        /// <summary>Eine Wanduhr, die nur weitergeht, wenn der Test es sagt.</summary>
        private sealed class SteppingClock : IClock
        {
            public DateTime UtcNow { get; private set; } = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
        }

        /// <summary>Eine feste Geräte-Kennung.</summary>
        private sealed class FixedDevice(Guid id) : IDeviceIdentity
        {
            public Guid DeviceId { get; } = id;
        }
    }
}
