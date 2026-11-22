using System.Runtime.InteropServices;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Steuert den Fortschrittsbalken unter dem Taskleisten-Symbol der Anwendung.
    ///
    /// Windows zeigt unterhalb von App-Icons in der Taskleiste einen farbigen Balken an,
    /// wenn eine App Fortschritt meldet. Das kennt man z.B. von Visual Studio während des Builds.
    /// Die Steuerung erfolgt über das COM-Interface <c>ITaskbarList3</c> aus der Windows Shell.
    ///
    /// Der Service ist als Singleton registriert; den Scan-Fortschritt reicht ihm der
    /// <c>ScanProgressIndicator</c> der Statusleiste weiter.
    /// </summary>
    public sealed class TaskbarProgressService
    {
        // Mapping in TaskbarProgressStateExtensions, damit testbar ohne COM.

        // COM-Instanz wird beim ersten Aufruf erzeugt und danach wiederverwendet.
        // null bedeutet: Initialisierung fehlgeschlagen oder noch nicht versucht.
        private ITaskbarList3? _taskbar;
        private bool _initAttempted;

        /// <summary>
        /// Zeigt einen bestimmten Fortschrittswert unter dem Taskleisten-Symbol an.
        /// </summary>
        /// <param name="percentComplete">Fortschritt in Prozent (0–100).</param>
        public void SetProgress(double percentComplete) => Apply((taskbar, hwnd) =>
        {
            taskbar.SetProgressState(hwnd, TaskbarProgressState.Normal.ToFlag());
            taskbar.SetProgressValue(hwnd, TaskbarProgressStateExtensions.ClampPercent(percentComplete), 100UL);
        });

        /// <summary>
        /// Zeigt einen unbestimmten (animierten) Fortschrittsbalken an.
        /// Geeignet für Phasen, in denen die Gesamtdauer noch nicht bekannt ist.
        /// </summary>
        public void SetIndeterminate() => Apply((taskbar, hwnd) =>
            taskbar.SetProgressState(hwnd, TaskbarProgressState.Indeterminate.ToFlag()));

        /// <summary>
        /// Entfernt den Fortschrittsbalken aus der Taskleiste.
        /// Wird nach Abschluss des Scans aufgerufen.
        /// </summary>
        public void Clear() => Apply((taskbar, hwnd) =>
            taskbar.SetProgressState(hwnd, TaskbarProgressState.None.ToFlag()));

        /// <summary>
        /// Führt einen Aufruf an der Taskleiste aus. Scheitert er, bleibt der Balken für den Rest
        /// der Sitzung aus; er ist eine optische Zugabe und darf keinen Aufrufer stören.
        /// </summary>
        /// <param name="call">Der Aufruf mit COM-Instanz und Fenster-Handle.</param>
        private void Apply(Action<ITaskbarList3, nint> call)
        {
            ITaskbarList3? taskbar = GetTaskbar();
            nint hwnd = GetHwnd();
            if (taskbar is null || hwnd == 0)
            {
                return;
            }

            try
            {
                call(taskbar, hwnd);
            }
            catch (ExternalException)
            {
                // COMException für einen HRESULT-Fehler der Shell, SEHException für einen Fehler
                // im nativen Teil.
                _taskbar = null;
            }
        }

        /// <summary>
        /// Gibt die COM-Instanz zurück, legt sie beim ersten Aufruf an.
        /// Bei Fehlern (z.B. kein Shell-Support) wird null zurückgegeben.
        /// </summary>
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

                // HrInit muss vor jedem anderen Aufruf erfolgen, denn es initialisiert die Shell-Integration.
                _taskbar.HrInit();
            }
            catch (ExternalException)
            {
                // Die Shell-Klasse ist nicht registriert oder HrInit scheitert.
                _taskbar = null;
            }
            catch (InvalidCastException)
            {
                // Die Klasse existiert, bietet die Schnittstelle aber nicht an (ältere Shell).
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

        /// <summary>
        /// Das ITaskbarList3-Interface aus der Windows Shell.
        /// Die Methodenreihenfolge im Interface entspricht exakt der COM-vtable.
        /// Das ist zwingend, sonst werden die falschen Methoden aufgerufen.
        ///
        /// Deklariert sind nur die Methoden bis SetProgressState; die nachfolgenden (RegisterTab,
        /// ThumbBar usw.) braucht die Anwendung nicht.
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
