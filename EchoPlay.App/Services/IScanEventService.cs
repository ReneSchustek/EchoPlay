using EchoPlay.Data.Entities.Library;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ermöglicht entkoppelte Benachrichtigung über Scan-Ereignisse.
    /// Registriert als Singleton, damit das Transient-ViewModel der lokalen Mediathek
    /// das Event auch nach einer Rücknavigation abonnieren kann.
    /// Problem ohne diesen Service: Der Scan-Callback war ein Closure auf das alte ViewModel –
    /// nach Navigation entstand ein neues ViewModel, das keine Callbacks mehr erhielt.
    /// </summary>
    public interface IScanEventService
    {
        /// <summary>
        /// Gibt an, ob gerade ein Bibliotheks-Scan läuft.
        /// Kann beim Navigieren zur Seite abgefragt werden, um den Scan-Zustand sofort korrekt anzuzeigen.
        /// </summary>
        bool IsScanRunning { get; }

        /// <summary>
        /// Wird ausgelöst, wenn eine Serie erfolgreich in der Datenbank synchronisiert wurde.
        /// Handler laufen auf dem UI-Thread, wenn <see cref="SyncService"/> <c>Progress&lt;T&gt;</c>
        /// für die Benachrichtigung verwendet.
        /// </summary>
        event Action<Series>? SeriesSynced;

        /// <summary>
        /// Markiert den Scan als laufend.
        /// Muss am Anfang von <see cref="ISyncService.SyncAsync"/> aufgerufen werden.
        /// </summary>
        void BeginScan();

        /// <summary>
        /// Markiert den Scan als abgeschlossen.
        /// Muss am Ende von <see cref="ISyncService.SyncAsync"/> aufgerufen werden (auch im finally-Block).
        /// </summary>
        void EndScan();

        /// <summary>
        /// Benachrichtigt alle Abonnenten über eine neu synchronisierte Serie.
        /// </summary>
        /// <param name="series">Die synchronisierte Serie.</param>
        void RaiseSeriesSynced(Series series);
    }
}
