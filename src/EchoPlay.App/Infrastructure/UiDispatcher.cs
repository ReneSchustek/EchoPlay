using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Holt die Warteschlange des Oberflächen-Threads für Ansichtsmodelle, die Arbeit aus
    /// Hintergrund-Threads an die Oberfläche weiterreichen.
    /// </summary>
    /// <remarks>
    /// Aufgerufen im Konstruktor, der auf dem Oberflächen-Thread läuft. Im Testprozess gibt es
    /// keine WinUI-Laufzeit; <see cref="DispatcherQueue.GetForCurrentThread"/> wirft dort eine
    /// <see cref="COMException"/>, und die Ansichtsmodelle arbeiten ohne Warteschlange direkt.
    /// </remarks>
    public static class UiDispatcher
    {
        /// <summary>
        /// Liefert die Warteschlange des aktuellen Threads.
        /// </summary>
        /// <returns>Die Warteschlange, oder <see langword="null"/> ohne WinUI-Laufzeit.</returns>
        public static DispatcherQueue? TryGetForCurrentThread()
        {
            try
            {
                return DispatcherQueue.GetForCurrentThread();
            }
            catch (COMException)
            {
                // Keine WinUI-Laufzeit im Prozess: Die Aufrufer arbeiten dann synchron.
                return null;
            }
        }
    }
}
