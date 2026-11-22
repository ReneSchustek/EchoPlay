using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Meldet entkoppelt, dass sich der Neuerscheinungen-Cache geändert hat.
    /// Als Singleton registriert, weil die Auslöser (Favorisieren, Auge-Umschalten) in
    /// kurzlebigen Scopes laufen, die Startseite aber ein Transient-ViewModel ist.
    /// </summary>
    /// <remarks>
    /// Ohne diese Meldung bliebe die bereits gerenderte Startseite auf dem alten Stand,
    /// bis der Nutzer erneut dorthin navigiert; der Hintergrund-Check würde unsichtbar bleiben.
    /// </remarks>
    public interface INewReleaseEventService
    {
        /// <summary>
        /// Wird ausgelöst, nachdem Einträge im Neuerscheinungen-Cache ergänzt oder entfernt wurden.
        /// Handler müssen selbst auf den UI-Thread wechseln, denn die Auslöser laufen im Hintergrund.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
        event Action? CacheChanged;

        /// <summary>Meldet allen Abonnenten, dass der Cache neu gelesen werden sollte.</summary>
        void NotifyCacheChanged();
    }
}
