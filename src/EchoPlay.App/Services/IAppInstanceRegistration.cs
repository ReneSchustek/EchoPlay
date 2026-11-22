namespace EchoPlay.App.Services
{
    /// <summary>
    /// Der Anspruch dieses Prozesses, die eine laufende EchoPlay-Instanz zu sein.
    /// </summary>
    /// <remarks>
    /// Ein zweiter Start gibt an den Inhaber des Anspruchs ab und beendet sich. Wer sich
    /// selbst neu startet, etwa für den Sprachwechsel, muss den Anspruch vorher freigeben,
    /// sonst hält der Nachfolger den alten Prozess für die laufende Instanz und beendet
    /// sich gleich wieder. In Tests ein Fake, weil die Windows-Laufzeit dort fehlt.
    /// </remarks>
    public interface IAppInstanceRegistration
    {
        /// <summary>Gibt den Anspruch frei, damit ein Nachfolgeprozess ihn übernehmen kann.</summary>
        void Release();

        /// <summary>
        /// Holt den Anspruch zurück, nachdem ein Neustart nicht zustande kam. Sonst liefe ab
        /// jetzt jeder weitere Start als eigene Instanz.
        /// </summary>
        void Reclaim();
    }
}
