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
    /// bis der Nutzer erneut dorthin navigiert – der Hintergrund-Check würde unsichtbar bleiben.
    /// </remarks>
    public interface INewReleaseEventService
    {
        /// <summary>
        /// Wird ausgelöst, nachdem Einträge im Neuerscheinungen-Cache ergänzt oder entfernt wurden.
        /// Handler müssen selbst auf den UI-Thread wechseln – die Auslöser laufen im Hintergrund.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Reine Signalmeldung ohne Nutzlast; ein EventArgs-Wrapper brächte keinen Informationsgewinn und würde nur Zeremonie hinzufügen.")]
        event Action? CacheChanged;

        /// <summary>Meldet allen Abonnenten, dass der Cache neu gelesen werden sollte.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "Raise-Methode ist der Publisher-Kontrakt des Singletons – nur Favoriten- und Auge-Toggle lösen das Event aus, Abonnenten dürfen es nicht selbst feuern (gleiche Linie wie IScanEventService).")]
        void RaiseCacheChanged();
    }
}
