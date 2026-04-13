using System.Diagnostics.CodeAnalysis;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Erweiterung für Log-Senken, die neue Einträge in Echtzeit melden können.
    /// Implementierende Klassen feuern <see cref="LogEntryAdded"/>, sobald ein neuer
    /// Eintrag geschrieben wurde – ohne Polling, ohne Timer.
    /// <para>
    /// Das Event wird außerhalb jedes internen Locks gefeuert, damit Subscriber
    /// keine Deadlocks verursachen können.
    /// </para>
    /// </summary>
    public interface ILiveLogSink
    {
        /// <summary>
        /// Wird ausgelöst, sobald ein neuer Log-Eintrag geschrieben wurde.
        /// Das Event wird auf dem Thread gefeuert, der <c>WriteAsync</c> aufgerufen hat –
        /// in der Regel ein Hintergrund-Thread. Subscriber müssen selbst auf den UI-Thread
        /// wechseln (z.B. via <c>DispatcherQueue.TryEnqueue</c>).
        /// </summary>
        [SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Action<T> ist in EchoPlay-Logger bewusst verwendet; EventHandler<T> wäre hier Overkill.")]
        event Action<LogEntry> LogEntryAdded;
    }
}
