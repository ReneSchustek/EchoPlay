using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für den lokalen Bibliothek-Sync.
    /// Ermöglicht die Entkopplung von ViewModels und Tests von der konkreten <see cref="SyncService"/>-Implementierung.
    /// </summary>

    public interface ISyncService
    {
        /// <summary>
        /// Startet den Sync-Vorgang und gibt eine Zusammenfassung des Ergebnisses zurück.
        /// </summary>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback – erhält <see cref="ScanProgress"/>-Objekte
        /// mit Text und prozentualem Fortschritt während Scan und Sync.
        /// </param>
        /// <param name="forceImportAll">
        /// Wenn <see langword="true"/>, werden alle gescannten Serien importiert, unabhängig von
        /// der <c>AutoImportAfterScan</c>-Einstellung. Wird von der Neu-Initialisierung verwendet,
        /// damit nach einem vollständigen Reset garantiert alle Serienordner neu angelegt werden.
        /// </param>
        /// <param name="onSeriesSynced">
        /// Optionaler Callback, der nach jeder DB-synchronisierten <see cref="Series"/> aufgerufen wird.
        /// Ermöglicht dem ViewModel, Serien sofort in der Liste anzuzeigen ohne auf das Gesamtergebnis
        /// zu warten. Wird auf dem SynchronizationContext des Aufrufers ausgeführt (UI-Thread).
        /// </param>
        /// <param name="cancellationToken">
        /// Optionaler Token zum Abbruch eines laufenden Scans, z.B. wenn der Nutzer die Mediathek-
        /// Seite verlässt oder die App schließt.
        /// </param>
        /// <returns>Zusammenfassung des Sync-Ergebnisses.</returns>
        Task<SyncResult> SyncAsync(
            IProgress<ScanProgress>? progress = null,
            bool forceImportAll = false,
            IProgress<Series>? onSeriesSynced = null,
            CancellationToken cancellationToken = default);
    }
}
