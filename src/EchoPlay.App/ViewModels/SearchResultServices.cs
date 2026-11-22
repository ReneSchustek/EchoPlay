using EchoPlay.App.Services;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Dienste, die eine Trefferkarte und die Suchseite für Import, Fehleranzeige, Texte und
    /// Cover brauchen. Sie treten immer gemeinsam auf und werden unverändert durchgereicht.
    /// </summary>
    public sealed record SearchResultServices
    {
        /// <summary>Führt den tatsächlichen Import durch.</summary>
        public required ImportService ImportService { get; init; }

        /// <summary>Zeigt Fehlermeldungen an.</summary>
        public required IErrorDialogService ErrorDialogService { get; init; }

        /// <summary>Für lokalisierte Fehlertexte.</summary>
        public required ILocalizationService LocalizationService { get; init; }

        /// <summary>
        /// Lädt das Cover über die zentrale Cover-Pipeline (DB-First, dann Provider-URL über
        /// <c>IHostRateLimiter</c> mit Foreground-Priorität). Null deaktiviert das Cover-Laden.
        /// </summary>
        public BackgroundCoverService? BackgroundCoverService { get; init; }
    }
}
