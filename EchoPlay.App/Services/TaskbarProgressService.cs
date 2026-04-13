using System.Runtime.InteropServices;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Steuert den Fortschrittsbalken unter dem Taskleisten-Symbol der Anwendung.
    ///
    /// Windows zeigt unterhalb von App-Icons in der Taskleiste einen farbigen Balken an,
    /// wenn eine App Fortschritt meldet – das kennt man z.B. von Visual Studio während des Builds.
    /// Die Steuerung erfolgt über das COM-Interface <c>ITaskbarList3</c> aus der Windows Shell.
    ///
    /// Der Service ist als Singleton registriert und wird von <see cref="EchoPlay.App.ViewModels.StatusBarViewModel"/>
    /// bei jedem Scan-Fortschritts-Update aufgerufen.
    /// </summary>
    public sealed class TaskbarProgressService
    {
        // Taskbar-Fortschrittsmodus: kein Balken, unbestimmt oder normaler Fortschritt
        private const int TBPF_NOPROGRESS    = 0x0;
        private const int TBPF_INDETERMINATE = 0x1;
        private const int TBPF_NORMAL        = 0x2;

        // COM-Instanz wird beim ersten Aufruf erzeugt und danach wiederverwendet.
        // null bedeutet: Initialisierung fehlgeschlagen oder noch nicht versucht.
        private ITaskbarList3? _taskbar;
        private bool _initAttempted;

        /// <summary>
        /// Zeigt einen bestimmten Fortschrittswert unter dem Taskleisten-Symbol an.
        /// </summary>
        /// <param name="percentComplete">Fortschritt in Prozent (0–100).</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ITaskbarList3 ist Win32-COM-Interop: native Shell-Fehler (RPC_E_*, HRESULT-Fehler) aus SetProgressState/SetProgressValue duerfen den Aufrufer nicht reissen. Taskleisten-Fortschritt ist optisch, '_taskbar' wird genullt, damit Folgeaufrufe still abbrechen.")]
        public void SetProgress(double percentComplete)
        {
            ITaskbarList3? taskbar = GetTaskbar();
            nint hwnd = GetHwnd();
            if (taskbar is null || hwnd == 0)
            {
                return;
            }

            try
            {
                taskbar.SetProgressState(hwnd, TBPF_NORMAL);
                taskbar.SetProgressValue(hwnd, (ulong)percentComplete, 100UL);
            }
            catch
            {
                // Taskleisten-Fortschritt ist ein optisches Feature – Fehler dürfen die App nicht beeinflussen.
                _taskbar = null;
            }
        }

        /// <summary>
        /// Zeigt einen unbestimmten (animierten) Fortschrittsbalken an.
        /// Geeignet für Phasen, in denen die Gesamtdauer noch nicht bekannt ist.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ITaskbarList3.SetProgressState (Win32-COM): native Shell-Fehler duerfen den Aufrufer nicht reissen; '_taskbar' wird genullt, damit Folgeaufrufe still abbrechen.")]
        public void SetIndeterminate()
        {
            ITaskbarList3? taskbar = GetTaskbar();
            nint hwnd = GetHwnd();
            if (taskbar is null || hwnd == 0)
            {
                return;
            }

            try
            {
                taskbar.SetProgressState(hwnd, TBPF_INDETERMINATE);
            }
            catch
            {
                _taskbar = null;
            }
        }

        /// <summary>
        /// Entfernt den Fortschrittsbalken aus der Taskleiste.
        /// Wird nach Abschluss des Scans aufgerufen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ITaskbarList3.SetProgressState (Win32-COM): native Shell-Fehler beim Zuruecksetzen des Fortschrittsbalkens duerfen den Aufrufer nicht reissen.")]
        public void Clear()
        {
            ITaskbarList3? taskbar = GetTaskbar();
            nint hwnd = GetHwnd();
            if (taskbar is null || hwnd == 0)
            {
                return;
            }

            try
            {
                taskbar.SetProgressState(hwnd, TBPF_NOPROGRESS);
            }
            catch
            {
                _taskbar = null;
            }
        }

        // ── Interne Hilfsmethoden ────────────────────────────────────────────────

        /// <summary>
        /// Gibt die COM-Instanz zurück, legt sie beim ersten Aufruf an.
        /// Bei Fehlern (z.B. kein Shell-Support) wird null zurückgegeben.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Einmalige COM-Activation von ITaskbarList3: auf Systemen ohne passende Shell-Unterstuetzung kann 'new TaskbarInstance()' oder 'HrInit' scheitern (COMException/InvalidCastException); das Feature wird dann still deaktiviert.")]
        private ITaskbarList3? GetTaskbar()
        {
            if (_initAttempted)
            {
                return _taskbar;
            }

            _initAttempted = true;

            try
            {
                _taskbar = (ITaskbarList3)new TaskbarInstance();

                // HrInit muss vor jedem anderen Aufruf erfolgen – initialisiert die Shell-Integration.
                _taskbar.HrInit();
            }
            catch
            {
                _taskbar = null;
            }

            return _taskbar;
        }

        /// <summary>
        /// Gibt das Window-Handle (HWND) des Hauptfensters zurück.
        /// 0 wenn das Fenster noch nicht bereit ist.
        /// </summary>
        private static nint GetHwnd()
        {
            if (App.MainWindow is null)
            {
                return 0;
            }

            return WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        }

        // ── COM-Definitionen ─────────────────────────────────────────────────────

        /// <summary>
        /// Das ITaskbarList3-Interface aus der Windows Shell.
        /// Die Methodenreihenfolge im Interface entspricht exakt der COM-vtable –
        /// das ist zwingend, sonst werden die falschen Methoden aufgerufen.
        ///
        /// Hier sind nur die Methoden bis SetProgressState definiert, weil wir
        /// die nachfolgenden (RegisterTab, ThumbBar, etc.) nicht benötigen.
        /// </summary>
        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList (Methoden 1–5)
            void HrInit();
            void AddTab(nint hwnd);
            void DeleteTab(nint hwnd);
            void ActivateTab(nint hwnd);
            void SetActiveAlt(nint hwnd);
            // ITaskbarList2 (Methode 6)
            void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            // ITaskbarList3 (Methoden 7–8)
            void SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);
            void SetProgressState(nint hwnd, int tbpFlags);
        }

        /// <summary>
        /// COM-CoClass für ITaskbarList3. Die GUID ist die Shell-CLSID der TaskbarList.
        /// </summary>
        [ComImport]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance { }
    }
}
