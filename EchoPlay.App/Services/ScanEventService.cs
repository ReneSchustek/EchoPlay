using EchoPlay.Data.Entities.Library;
using System;
using System.Threading;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Implementierung des <see cref="IScanEventService"/>.
    /// Als Singleton registriert – überlebt Navigation zwischen Seiten.
    /// <see cref="IsScanRunning"/> ist thread-sicher via <c>Interlocked</c>,
    /// da <see cref="SyncService"/> den Scan auf einem Hintergrundthread starten kann.
    /// </summary>

    public sealed class ScanEventService : IScanEventService
    {
        private int _isRunning;

        /// <inheritdoc/>
        public bool IsScanRunning => _isRunning == 1;

        /// <inheritdoc/>
        public event Action<Series>? SeriesSynced;

        /// <inheritdoc/>
        public void BeginScan()
        {
            _ = Interlocked.Exchange(ref _isRunning, 1);
        }

        /// <inheritdoc/>
        public void EndScan()
        {
            _ = Interlocked.Exchange(ref _isRunning, 0);
        }

        /// <inheritdoc/>

        /// <param name="series">Parameter <c>series</c>.</param>
        public void RaiseSeriesSynced(Series series)
        {
            SeriesSynced?.Invoke(series);
        }
    }
}
