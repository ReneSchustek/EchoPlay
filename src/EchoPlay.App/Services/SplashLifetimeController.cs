namespace EchoPlay.App.Services
{
    /// <summary>
    /// Garantiert, dass der Splash mindestens <see cref="MinimumDuration"/> sichtbar bleibt,
    /// auch wenn die Initialisierung schneller fertig wird (warmer Cache, Re-Start).
    /// Vermeidet das Aufflackern bei sehr schnellen App-Starts und gibt dem Branding
    /// genug Sichtzeit.
    /// </summary>
    public sealed class SplashLifetimeController
    {
        /// <summary>
        /// Mindest-Anzeigedauer des Startbilds.
        /// </summary>
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(1500);

        private readonly TimeProvider _time;
        private readonly long _startTimestamp;

        /// <summary>
        /// Beginnt die Zeitmessung sofort. Soll im Konstruktor des Splash-Fensters
        /// oder unmittelbar danach aufgerufen werden.
        /// </summary>
        /// <param name="timeProvider">Zeitquelle für Messung und Wartezeit; ohne Angabe die Systemzeit.</param>
        public SplashLifetimeController(TimeProvider? timeProvider = null)
        {
            _time = timeProvider ?? TimeProvider.System;
            _startTimestamp = _time.GetTimestamp();
        }

        /// <summary>
        /// Wartet, falls nötig, bis die <see cref="MinimumDuration"/> erreicht ist.
        /// Kehrt sofort zurück, wenn die Mindestzeit bereits überschritten wurde
        /// oder wenn das Token abgebrochen wird.
        /// </summary>
        /// <param name="cancellationToken">Optionaler Abbruch-Token (Splash-Lebenszyklus).</param>
        public async Task WaitForMinimumDurationAsync(CancellationToken cancellationToken = default)
        {
            TimeSpan elapsed = _time.GetElapsedTime(_startTimestamp);
            if (elapsed >= MinimumDuration)
            {
                return;
            }

            TimeSpan remaining = MinimumDuration - elapsed;
            try
            {
                await Task.Delay(remaining, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Cancellation beim Splash-Schließen ist erwartetes Verhalten, daher wird der Abbruch verschluckt.
            }
        }
    }
}
