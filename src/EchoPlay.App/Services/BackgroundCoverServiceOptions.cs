using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Konfigurierbare Optionen für den Hintergrund-Cover-Dienst.
    /// </summary>
    public sealed class BackgroundCoverServiceOptions
    {
        /// <summary>
        /// Intervall zwischen aufeinanderfolgenden Durchläufen. Eine halbe Stunde genügt: Der
        /// Import lädt die Cover seiner Folgen selbst, der Dienst trägt nur nach, was dabei oder
        /// nach dem Einlesen noch fehlt.
        /// </summary>
        public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Wartezeit vor dem ersten Durchlauf nach App-Start. Drei Sekunden lassen dem Fenster
        /// Zeit für das erste Zeichnen, bevor der Dienst Datenbank und Netz beansprucht.
        /// </summary>
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(3);
    }
}
