using EchoPlay.Data.Entities.Library;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Meldet Scan-Ereignisse an alle, die zuhören, ohne dass Scanner und Ansicht einander kennen.
    /// Registriert als Singleton, damit das Transient-ViewModel der lokalen Mediathek
    /// das Event auch nach einer Rücknavigation abonnieren kann. Ein Rückruf, der am
    /// Ansichtsmodell hinge, erreichte nach einer Navigation das alte statt des neuen.
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
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
        void NotifySeriesSynced(Series series);
    }
}
