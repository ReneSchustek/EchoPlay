using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
        /// Mindest-Anzeigedauer gemäß <c>splashscreen.md</c>.
        /// </summary>
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(1500);

        private readonly Stopwatch _stopwatch;

        /// <summary>
        /// Startet den Stopwatch sofort. Soll im Konstruktor des Splash-Fensters
        /// oder unmittelbar danach aufgerufen werden.
        /// </summary>

        public SplashLifetimeController()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Wartet, falls nötig, bis die <see cref="MinimumDuration"/> erreicht ist.
        /// Kehrt sofort zurück, wenn die Mindestzeit bereits überschritten wurde
        /// oder wenn das Token gecanceled wird.
        /// </summary>
        /// <param name="cancellationToken">Optionaler Abbruch-Token (Splash-Lebenszyklus).</param>

        public async Task WaitForMinimumDurationAsync(CancellationToken cancellationToken = default)
        {
            TimeSpan elapsed = _stopwatch.Elapsed;
            if (elapsed >= MinimumDuration)
            {
                return;
            }

            TimeSpan remaining = MinimumDuration - elapsed;
            try
            {
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Cancellation beim Splash-Schließen ist erwartetes Verhalten — verschluckter Abbruch.
            }
        }
    }
}
