namespace EchoPlay.App.Services
{
    /// <summary>
    /// Führt alle Startup-Validierungen während des Begrüßungsbildschirms durch.
    /// Prüft Online-Konnektivität und lokale Bibliothek, räumt den Zwischenspeicher auf und
    /// frischt die Neuerscheinungen darin auf; die Startseite liest sie von dort.
    /// </summary>
    public interface IStartupValidator
    {
        /// <summary>
        /// Führt alle Startup-Checks aus und gibt ein <see cref="StartupResult"/> zurück.
        /// </summary>
        /// <param name="onStatus">Callback für Statusmeldungen, die im Splash angezeigt werden.</param>
        /// <param name="cancellationToken">Optionaler Token zum Abbruch (z.B. wenn der Nutzer das Splash-Fenster schließt).</param>
        /// <returns>Das Ergebnis aller Startup-Validierungen.</returns>
        Task<StartupResult> ValidateAsync(
            Action<string>? onStatus = null,
            CancellationToken cancellationToken = default);
    }
}
