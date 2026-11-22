using EchoPlay.App.Services;
using System;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Takt der Wiedergabe ohne echten Zeitgeber: Jeder Aufruf von <c>Tick</c> steht
    /// für 500 ms.
    /// </summary>
    public sealed class PlaybackTickerTests : IDisposable
    {
        private readonly PlaybackTicker _ticker = new();
        private int _saves;
        private int _sleeps;
        private int _ticks;

        /// <summary>Zählt die drei Ereignisse des Takts mit.</summary>
        public PlaybackTickerTests()
        {
            _ticker.SaveDue += (_, _) => _saves++;
            _ticker.SleepTimerElapsed += (_, _) => _sleeps++;
            _ticker.Ticked += (_, _) => _ticks++;
        }

        /// <inheritdoc/>
        public void Dispose() => _ticker.Dispose();

        [Fact]
        public void Tick_SavesEveryThirtySeconds()
        {
            TickTimes(59);
            Assert.Equal(0, _saves);

            TickTimes(1);
            Assert.Equal(1, _saves);
            Assert.Equal(60, _ticks);
        }

        [Fact]
        public void RestartAutoSave_StartsTheThirtySecondsAgain()
        {
            TickTimes(59);
            _ticker.RestartAutoSave();
            TickTimes(59);

            Assert.Equal(0, _saves);
        }

        [Fact]
        public void Tick_WhenSleepTimerRunsOut_RaisesSleepInsteadOfTick()
        {
            _ticker.SetSleepTimer(TimeSpan.FromSeconds(1));

            TickTimes(1);
            Assert.Equal(0, _sleeps);
            Assert.Equal(1, _ticks);

            TickTimes(1);
            Assert.Equal(1, _sleeps);
            Assert.Equal(1, _ticks);
        }

        [Fact]
        public void Reset_ClearsSleepTimerAndSaveCount()
        {
            _ticker.SetSleepTimer(TimeSpan.FromMinutes(5));
            TickTimes(59);

            _ticker.Reset();
            TickTimes(59);

            Assert.Null(_ticker.SleepTimerRemaining);
            Assert.Equal(0, _saves);
        }

        [Fact]
        public void StartAndStop_DoNotRaiseEventsByThemselves()
        {
            _ticker.Start();
            _ticker.Stop();

            Assert.Equal(0, _saves + _sleeps + _ticks);
        }

        private void TickTimes(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _ticker.Tick();
            }
        }
    }
}
