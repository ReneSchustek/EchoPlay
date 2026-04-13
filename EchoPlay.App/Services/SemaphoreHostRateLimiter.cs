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
    /// </summary>
    internal sealed class SemaphoreHostRateLimiter : IHostRateLimiter
    {
        private readonly IReadOnlyDictionary<string, TimeSpan> _intervals;
        private readonly TimeSpan _defaultInterval;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCalls = new();

        /// <summary>
        /// Erstellt einen neuen Rate-Limiter mit Host-spezifischen Intervallen.
        /// </summary>
        /// <param name="intervals">Minimum-Intervall pro Hostname.</param>
        /// <param name="defaultInterval">Fallback-Intervall für unbekannte Hosts.</param>
        public SemaphoreHostRateLimiter(
            IReadOnlyDictionary<string, TimeSpan> intervals,
            TimeSpan? defaultInterval = null)
        {
            _intervals = intervals;
            _defaultInterval = defaultInterval ?? TimeSpan.FromSeconds(1);
        }

        /// <inheritdoc/>
        public async Task WaitAsync(string host, CancellationToken ct = default)
        {
            SemaphoreSlim semaphore = _semaphores.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                TimeSpan interval = _intervals.TryGetValue(host, out TimeSpan configured)
                    ? configured
                    : _defaultInterval;

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
                semaphore.Release();
            }
        }
    }
}
