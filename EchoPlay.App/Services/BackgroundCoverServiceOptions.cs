using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Konfigurierbare Optionen für den Hintergrund-Cover-Dienst.
    /// </summary>

    public sealed class BackgroundCoverServiceOptions
    {
        /// <summary>Intervall zwischen aufeinanderfolgenden Durchläufen.</summary>
        public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>Wartezeit vor dem ersten Durchlauf nach App-Start.</summary>
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(3);
    }
}
