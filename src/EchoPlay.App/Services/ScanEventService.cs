using EchoPlay.Data.Entities.Library;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Implementierung des <see cref="IScanEventService"/>.
    /// Als Singleton registriert, überlebt Navigation zwischen Seiten.
    /// <see cref="IsScanRunning"/> ist thread-sicher via <c>Interlocked</c>,
    /// da <see cref="SyncService"/> den Scan auf einem Hintergrundthread starten kann.
    /// </summary>
    public sealed class ScanEventService : IScanEventService
    {
        private int _isRunning;

        /// <inheritdoc/>
        public bool IsScanRunning => Volatile.Read(ref _isRunning) == 1;

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
        /// <param name="series">Die Serie, deren Abgleich gerade fertig wurde.</param>
        public void NotifySeriesSynced(Series series)
        {
            SeriesSynced?.Invoke(series);
        }
    }
}
