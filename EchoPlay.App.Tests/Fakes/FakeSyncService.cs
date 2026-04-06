using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISyncService"/>.
    /// Gibt ein fest konfiguriertes <see cref="SyncResult"/> zurück und zeichnet Aufrufe auf.
    /// </summary>
    internal sealed class FakeSyncService : ISyncService
    {
        private readonly SyncResult _result;
        private readonly Exception? _exception;

        /// <summary>
        /// Erstellt den Fake mit einem festen Ergebnis.
        /// </summary>
        /// <param name="result">Das zurückzugebende Ergebnis.</param>
        /// <param name="exception">Optional: wirft diese Exception statt ein Ergebnis zu liefern.</param>
        public FakeSyncService(SyncResult? result = null, Exception? exception = null)
        {
            _result    = result ?? new SyncResult();
            _exception = exception;
        }

        /// <summary>Gibt an, wie oft <see cref="SyncAsync"/> aufgerufen wurde.</summary>
        public int SyncCallCount { get; private set; }

        /// <summary>Gibt an, ob der letzte Aufruf <c>forceImportAll = true</c> gesetzt hatte.</summary>
        public bool LastForceImportAll { get; private set; }

        /// <inheritdoc/>
        public Task<SyncResult> SyncAsync(
            IProgress<ScanProgress>? progress = null,
            bool forceImportAll = false,
            IProgress<Series>? onSeriesSynced = null)
        {
            SyncCallCount++;
            LastForceImportAll = forceImportAll;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result);
        }
    }
}
