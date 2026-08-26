using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Rate-Limiter, der pro Host ein konfigurierbares Minimum-Intervall zwischen
    /// aufeinanderfolgenden Aufrufen erzwingt. Thread-safe dank <see cref="SemaphoreSlim"/>
    /// pro Host und atomarem Zeitstempel-Tracking.
    /// Unterscheidet zusätzlich zwischen <see cref="CoverFetchPriority.Foreground"/>- und
    /// <see cref="CoverFetchPriority.Background"/>-Consumern: solange eine Foreground-Anfrage
    /// auf ihren Slot wartet oder gerade läuft, pausieren Background-Anfragen, damit die
    /// sichtbare UI das HTTP-Kontingent und Rate-Limit-Fenster zuerst bekommt.
    /// </summary>
    public sealed class SemaphoreHostRateLimiter : IHostRateLimiter
    {
        private readonly IReadOnlyDictionary<string, TimeSpan> _intervals;
        private readonly IReadOnlyDictionary<string, TimeSpan> _suffixIntervals;
        private readonly TimeSpan _defaultInterval;
        private readonly ConcurrentDictionary<string, TimeSpan> _resolvedIntervals = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCalls = new();
        private int _foregroundPending;
        private bool _disposed;

        // Polling-Intervall, in dem ein Background-Consumer prüft, ob die Foreground-Priorität
        // beendet ist. Klein genug, dass sich kein menschlich spürbares Latenz-Bucket bildet,
        // groß genug, damit der Thread-Pool keinen Spin aufbaut.
        private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Erstellt einen neuen Rate-Limiter mit Host-spezifischen Intervallen.
        /// </summary>
        /// <param name="intervals">Minimum-Intervall pro Hostname.</param>
        /// <param name="defaultInterval">Fallback-Intervall für unbekannte Hosts.</param>
        /// <param name="suffixIntervals">
        /// Minimum-Intervall pro Host-Endung, etwa <c>.mzstatic.com</c>. Greift, wenn kein
        /// exakter Hostname passt. Auslieferungsnetze für Bilder verteilen ihre Anfragen auf
        /// wechselnde Rechnernamen — die alle einzeln einzutragen wäre eine Liste, die bei
        /// jeder Umstellung der Gegenstelle veraltet.
        /// </param>
        public SemaphoreHostRateLimiter(
            IReadOnlyDictionary<string, TimeSpan> intervals,
            TimeSpan? defaultInterval = null,
            IReadOnlyDictionary<string, TimeSpan>? suffixIntervals = null)
        {
            _intervals = intervals;
            _suffixIntervals = suffixIntervals ?? new Dictionary<string, TimeSpan>();
            _defaultInterval = defaultInterval ?? TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Ermittelt das Mindestintervall für einen Host: exakter Eintrag, dann Endung,
        /// zuletzt der Standardwert. Das Ergebnis wird je Host gemerkt.
        /// </summary>
        /// <param name="host">Der Hostname.</param>
        /// <returns>Das Mindestintervall zwischen zwei Anfragen an diesen Host.</returns>
        private TimeSpan ResolveInterval(string host)
        {
            return _resolvedIntervals.GetOrAdd(host, name =>
            {
                if (_intervals.TryGetValue(name, out TimeSpan configured))
                {
                    return configured;
                }

                foreach (KeyValuePair<string, TimeSpan> suffix in _suffixIntervals)
                {
                    if (name.EndsWith(suffix.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return suffix.Value;
                    }
                }

                return _defaultInterval;
            });
        }

        /// <inheritdoc/>
        /// <param name="host">Hostname des Anbieters — die Begrenzung gilt pro Host, nicht global.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        public Task WaitAsync(string host, CancellationToken ct = default)
            => WaitAsync(host, CoverFetchPriority.Background, ct);

        /// <inheritdoc/>
        /// <param name="host">Hostname des Anbieters — die Begrenzung gilt pro Host, nicht global.</param>
        /// <param name="priority">Vorrang der Anfrage: Vordergrund-Anfragen bekommen einen eigenen Slot.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        public async Task WaitAsync(string host, CoverFetchPriority priority, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (priority == CoverFetchPriority.Foreground)
            {
                _ = Interlocked.Increment(ref _foregroundPending);
                try
                {
                    await WaitForSlotAsync(host, ct).ConfigureAwait(false);
                }
                finally
                {
                    _ = Interlocked.Decrement(ref _foregroundPending);
                }

                return;
            }

            // Background pausiert, solange Foreground-Anfragen im Flug sind.
            while (Volatile.Read(ref _foregroundPending) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(BackgroundPollInterval, ct).ConfigureAwait(false);
            }

            await WaitForSlotAsync(host, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Wartet den konfigurierten Mindestabstand seit dem letzten Aufruf für
        /// <paramref name="host"/> ab und setzt den Zeitstempel neu.
        /// </summary>
        /// <param name="host">Hostname des Anbieters — die Begrenzung gilt pro Host, nicht global.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        private async Task WaitForSlotAsync(string host, CancellationToken ct)
        {
            SemaphoreSlim semaphore = _semaphores.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                TimeSpan interval = ResolveInterval(host);

                if (_lastCalls.TryGetValue(host, out DateTimeOffset lastCall))
                {
                    TimeSpan elapsed = DateTimeOffset.UtcNow - lastCall;
                    TimeSpan remaining = interval - elapsed;

                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, ct).ConfigureAwait(false);
                    }
                }

                _lastCalls[host] = DateTimeOffset.UtcNow;
            }
            finally
            {
                _ = semaphore.Release();
            }
        }

        /// <summary>
        /// Gibt alle gehaltenen <see cref="SemaphoreSlim"/>-Handles frei. Nach
        /// Dispose wirft <see cref="WaitAsync(string, CancellationToken)"/> <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (SemaphoreSlim semaphore in _semaphores.Values)
            {
                semaphore.Dispose();
            }
            _semaphores.Clear();
        }
    }
}
