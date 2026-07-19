using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Logischer Fortschritts-Modus fuer den Taskleisten-Balken. Mapping zu
    /// den ITaskbarList3-COM-Flags liegt in <see cref="TaskbarProgressStateExtensions"/>.
    /// </summary>
    public enum TaskbarProgressState
    {
        /// <summary>Kein Balken (NOPROGRESS).</summary>
        None = 0,

        /// <summary>Animierter unbestimmter Balken (INDETERMINATE).</summary>
        Indeterminate = 1,

        /// <summary>Bestimmter Fortschrittsbalken (NORMAL).</summary>
        Normal = 2
    }

    /// <summary>
    /// Mapping zwischen <see cref="TaskbarProgressState"/> und den Win32-COM-Flags
    /// sowie die Prozent-Clamp-Logik. Ohne COM-Aufruf testbar.
    /// </summary>
    public static class TaskbarProgressStateExtensions
    {
        /// <summary>Konvertiert in das von ITaskbarList3 erwartete int-Flag.</summary>
        public static int ToFlag(this TaskbarProgressState state) => state switch
        {
            TaskbarProgressState.None => 0x0,
            TaskbarProgressState.Indeterminate => 0x1,
            TaskbarProgressState.Normal => 0x2,
            _ => 0x0
        };

        /// <summary>
        /// Klemmt einen Fortschrittswert auf das von ITaskbarList3 akzeptierte
        /// 0-100-Intervall. NaN und negative Werte werden zu 0, &gt;100 zu 100.
        /// </summary>
        public static ulong ClampPercent(double percentComplete)
        {
            if (double.IsNaN(percentComplete) || percentComplete < 0)
            {
                return 0UL;
            }
            if (percentComplete > 100)
            {
                return 100UL;
            }
            return (ulong)Math.Round(percentComplete);
        }
    }
}
