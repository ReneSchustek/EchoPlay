using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Künstliche Uhr, die mitzählt, wie oft ein Zeitgeber gestellt wurde und wie oft einer
    /// ausgelöst hat. Damit erkennt der <see cref="ClockDriver"/>, ob die Pipeline schon auf die
    /// Uhr wartet oder noch rechnet, statt das an der Geschwindigkeit des Rechners zu raten.
    /// </summary>
    internal sealed class ObservedClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private long _armed;
        private long _fired;

        /// <summary>Wie oft ein Zeitgeber auf eine endliche Frist gestellt wurde.</summary>
        public long ArmedCount => Interlocked.Read(ref _armed);

        /// <summary>Wie oft ein Zeitgeber ausgelöst hat.</summary>
        public long FiredCount => Interlocked.Read(ref _fired);

        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer inner = base.CreateTimer(
                s =>
                {
                    _ = Interlocked.Increment(ref _fired);
                    callback(s);
                },
                state,
                dueTime,
                period);
            CountIfArmed(dueTime);
            return new ObservedTimer(inner, this);
        }

        private void CountIfArmed(TimeSpan dueTime)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                _ = Interlocked.Increment(ref _armed);
            }
        }

        /// <summary>
        /// Hülle um den Zeitgeber der künstlichen Uhr. Polly stellt vorhandene Zeitgeber über
        /// <see cref="ITimer.Change"/> neu; das zählt wie ein neu gestellter.
        /// </summary>
        private sealed class ObservedTimer(ITimer inner, ObservedClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                clock.CountIfArmed(dueTime);
                return inner.Change(dueTime, period);
            }

            public void Dispose() => inner.Dispose();

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
