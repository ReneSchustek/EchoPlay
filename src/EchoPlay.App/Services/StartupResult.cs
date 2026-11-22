using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ergebnis der Startup-Validierung, die während des Begrüßungsbildschirms läuft.
    /// Die Startseite liest daraus, ob sie offline arbeitet und welche Hinweise sie zeigt;
    /// ihre Daten lädt sie selbst.
    /// </summary>
    public sealed class StartupResult
    {
        /// <summary>
        /// Gibt an, ob eine Internetverbindung verfügbar ist.
        /// Bei <see langword="false"/> blendet die Startseite die Neuerscheinungen aus.
        /// </summary>
        public bool IsOnlineAvailable { get; init; }

        /// <summary>
        /// Gibt an, ob das konfigurierte lokale Bibliotheksverzeichnis erreichbar und lesbar ist.
        /// Der Wert steht im Startprotokoll; den Hinweis für den Nutzer trägt <see cref="LocalLibraryHintText"/>.
        /// </summary>
        public bool IsLocalLibraryAvailable { get; init; }

        /// <summary>
        /// Hinweistext, wenn die Online-Verbindung nicht hergestellt werden konnte.
        /// <see langword="null"/> bedeutet: kein Problem oder Online nicht konfiguriert.
        /// </summary>
        public string? OnlineHintText { get; init; }

        /// <summary>
        /// Hinweistext, wenn das lokale Verzeichnis nicht erreichbar ist.
        /// <see langword="null"/> bedeutet: kein Problem oder Lokal nicht konfiguriert.
        /// </summary>
        public string? LocalLibraryHintText { get; init; }

        /// <summary>
        /// Die beim Start geladenen Anwendungseinstellungen.
        /// </summary>
        public AppSettings Settings { get; init; } = new();
    }
}
