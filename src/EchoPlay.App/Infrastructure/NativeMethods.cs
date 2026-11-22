using System.Runtime.InteropServices;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Die Windows-Aufrufe, für die das App SDK keinen eigenen Weg anbietet.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Erlaubt dem angegebenen Prozess, sein Fenster in den Vordergrund zu holen.
        /// </summary>
        /// <remarks>
        /// Windows lässt nur den Prozess ein Fenster nach vorn holen, der gerade bedient wird.
        /// Das ist der gerade gestartete zweite Prozess, nicht die laufende Instanz, die das
        /// Fenster besitzt. Ohne diese Freigabe blinkt dort nur die Taskleiste.
        /// </remarks>
        /// <param name="processId">Kennung des Prozesses, der die Erlaubnis bekommt.</param>
        /// <returns><see langword="true"/>, wenn Windows die Erlaubnis erteilt hat.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(uint processId);
    }
}
