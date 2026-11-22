using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Abgleich der lokalen Bibliothek mit der Datenbank. ViewModels und Tests hängen an dieser
    /// Schnittstelle statt an <see cref="SyncService"/>.
    /// </summary>
    public interface ISyncService
    {
        /// <summary>
        /// Startet den Sync-Vorgang und gibt eine Zusammenfassung des Ergebnisses zurück.
        /// </summary>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback, erhält <see cref="ScanProgress"/>-Objekte
        /// mit Text und prozentualem Fortschritt während Scan und Sync.
        /// </param>
        /// <param name="forceImportAll">
        /// Wenn <see langword="true"/>, werden alle gescannten Serien importiert, unabhängig von
        /// der <c>AutoImportAfterScan</c>-Einstellung. Wird von der Neu-Initialisierung verwendet,
        /// damit nach einem vollständigen Reset garantiert alle Serienordner neu angelegt werden.
        /// </param>
        /// <param name="onSeriesSynced">
        /// Optionaler Callback, der nach jeder DB-synchronisierten <see cref="Series"/> aufgerufen wird.
        /// Damit zeigt das ViewModel Serien sofort in der Liste an, ohne auf das Gesamtergebnis
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
