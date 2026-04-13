using EchoPlay.Core.Abstractions.Time;

namespace EchoPlay.Data.Internal
{
    /// <summary>
    /// Statischer Zeit-Holder für Entitäten und DataServices. BaseEntity und Co. haben keinen
    /// DI-Zugang; deshalb wird hier ein austauschbarer <see cref="IClock"/> bereitgestellt.
    /// Tests können die Uhr über <see cref="Current"/> umstellen, Produktion nutzt <see cref="SystemClock"/>.
    /// </summary>
    public static class EntityClock
    {
        /// <summary>Aktuell verwendete Uhr – per Default die System-Uhr.</summary>
        public static IClock Current { get; set; } = new SystemClock();
    }
}
