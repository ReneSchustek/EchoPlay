using EchoPlay.App.Services;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer das Mapping zwischen <see cref="TaskbarProgressState"/> und den
    /// ITaskbarList3-COM-Flags sowie die Prozent-Clamp-Logik.
    /// </summary>
    public sealed class TaskbarProgressStateTests
    {
        [Theory]
        [InlineData(TaskbarProgressState.None, 0x0)]
        [InlineData(TaskbarProgressState.Indeterminate, 0x1)]
        [InlineData(TaskbarProgressState.Normal, 0x2)]
        public void ToFlag_ReturnsExpectedComFlag(TaskbarProgressState state, int expectedFlag)
        {
            Assert.Equal(expectedFlag, state.ToFlag());
        }

        [Theory]
        [InlineData(-5.0, 0UL)]
        [InlineData(0.0, 0UL)]
        [InlineData(50.0, 50UL)]
        [InlineData(100.0, 100UL)]
        [InlineData(150.0, 100UL)]
        [InlineData(double.NaN, 0UL)]
        public void ClampPercent_StaysInZeroToHundredRange(double input, ulong expected)
        {
            Assert.Equal(expected, TaskbarProgressStateExtensions.ClampPercent(input));
        }

        [Fact]
        public void ClampPercent_RoundsToNearestUlong()
        {
            // Math.Round(49.6) == 50; ITaskbarList3 erwartet ulong.
            Assert.Equal(50UL, TaskbarProgressStateExtensions.ClampPercent(49.6));
        }
    }
}
