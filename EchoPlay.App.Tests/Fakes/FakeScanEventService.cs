using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using System;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Testisoliertes Fake für <see cref="IScanEventService"/>.
    /// Ermöglicht Unit-Tests von <see cref="EchoPlay.App.ViewModels.MediathekLokalViewModel"/>
    /// und <see cref="SyncService"/>, ohne einen echten Singleton-Service zu benötigen.
    /// Alle Aufrufe werden aufgezeichnet, damit Tests das erwartete Verhalten prüfen können.
    /// </summary>
    public sealed class FakeScanEventService : IScanEventService
    {
        /// <inheritdoc/>
        public bool IsScanRunning { get; private set; }

        /// <inheritdoc/>
        public event Action<Series>? SeriesSynced;

        /// <summary>Anzahl der <see cref="BeginScan"/>-Aufrufe.</summary>
        public int BeginScanCallCount { get; private set; }

        /// <summary>Anzahl der <see cref="EndScan"/>-Aufrufe.</summary>
        public int EndScanCallCount { get; private set; }

        /// <inheritdoc/>
        public void BeginScan()
        {
            IsScanRunning = true;
            BeginScanCallCount++;
        }

        /// <inheritdoc/>
        public void EndScan()
        {
            IsScanRunning = false;
            EndScanCallCount++;
        }

        /// <inheritdoc/>
        public void RaiseSeriesSynced(Series series)
        {
            SeriesSynced?.Invoke(series);
        }
    }
}
