using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Der Takt der Wiedergabe: alle 500 ms ein Fortschritt für die Anzeige, alle dreißig Sekunden
    /// eine fällige Sicherung des Hörstands, und das Ablaufen des Einschlaf-Timers.
    /// </summary>
    internal sealed class PlaybackTicker : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

        // Sicherung alle 30 Sekunden während aktiver Wiedergabe (500 ms × 60 Ticks): Die Stelle
        // geht auch dann nicht verloren, wenn die Anwendung hart beendet wird.
        private const int AutoSaveIntervalTicks = 60;

        private readonly System.Timers.Timer _timer = new(Interval.TotalMilliseconds);
        private readonly SleepTimer _sleepTimer = new();
        private readonly object _tickLock = new();
        private int _autoSaveTick;

        /// <summary>Verbindet den Takt mit seinem Zeitgeber.</summary>
        public PlaybackTicker()
        {
            _timer.Elapsed += OnElapsed;
        }

        /// <summary>Der Hörstand ist zu sichern.</summary>
        public event EventHandler? SaveDue;

        /// <summary>Der Einschlaf-Timer ist abgelaufen; die Wiedergabe soll anhalten.</summary>
        public event EventHandler? SleepTimerElapsed;

        /// <summary>Ein gewöhnlicher Takt: Position und Restzeit haben sich geändert.</summary>
        public event EventHandler? Ticked;

        /// <summary>Verbleibende Zeit des Einschlaf-Timers; null, wenn keiner läuft.</summary>
        public TimeSpan? SleepTimerRemaining => _sleepTimer.Remaining;

        /// <summary>Startet den Takt.</summary>
        public void Start() => _timer.Start();

        /// <summary>Hält den Takt an, ohne Zähler und Einschlaf-Timer zu verändern.</summary>
        public void Stop() => _timer.Stop();

        /// <summary>Setzt den Einschlaf-Timer oder schaltet ihn mit null ab.</summary>
        /// <param name="duration">Zeit bis zur Pause.</param>
        public void SetSleepTimer(TimeSpan? duration) => _sleepTimer.Set(duration);

        /// <summary>Beginnt die dreißig Sekunden bis zur nächsten Sicherung neu.</summary>
        public void RestartAutoSave()
        {
            lock (_tickLock) { _autoSaveTick = 0; }
        }

        /// <summary>Hält den Takt an und löscht Einschlaf-Timer und Zähler.</summary>
        public void Reset()
        {
            _timer.Stop();
            _sleepTimer.Set(null);
            RestartAutoSave();
        }

        /// <inheritdoc/>
        public void Dispose() => _timer.Dispose();

        private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e) => Tick();

        /// <summary>
        /// Ein Schritt des Takts. Der Zeitgeber ruft ihn alle 500 ms; Tests rufen ihn direkt, damit
        /// keine echte Zeit vergehen muss.
        /// </summary>
        internal void Tick()
        {
            bool shouldSave = false;

            lock (_tickLock)
            {
                _autoSaveTick++;
                if (_autoSaveTick >= AutoSaveIntervalTicks)
                {
                    _autoSaveTick = 0;
                    shouldSave = true;
                }
            }

            bool shouldPause = _sleepTimer.Tick(Interval);

            if (shouldSave)
            {
                SaveDue?.Invoke(this, EventArgs.Empty);
            }

            if (shouldPause)
            {
                SleepTimerElapsed?.Invoke(this, EventArgs.Empty);
                return;
            }

            Ticked?.Invoke(this, EventArgs.Empty);
        }
    }
}
