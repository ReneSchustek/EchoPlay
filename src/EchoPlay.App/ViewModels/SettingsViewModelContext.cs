using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Dienste für <see cref="SettingsViewModel"/>.
    /// Dieselbe Form wie <see cref="LocalLibraryViewModelContext"/>: Sechzehn einzelne
    /// Konstruktor-Parameter wären an der Aufrufstelle nicht auseinanderzuhalten;
    /// zwei vertauschte Dienste gleichen Typs bemerkte kein Compiler.
    /// Die Reiter-Ansichtsmodelle entstehen intern und gehören nicht hierher.
    /// </summary>
    internal sealed record SettingsViewModelContext
    {
        /// <summary>Für datenbanknahe Zugriffe beim Laden und Speichern.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Wendet ein Farbschema sofort an.</summary>
        public required IThemeService ThemeService { get; init; }

        /// <summary>Gleicht die lokale Mediathek ab.</summary>
        public required ISyncService SyncService { get; init; }

        /// <summary>Zeigt Fehlerhinweise.</summary>
        public required IErrorDialogService ErrorDialogService { get; init; }

        /// <summary>Fragt beim Verlassen mit ungespeicherten Änderungen nach.</summary>
        public required IConfirmationDialogService ConfirmationDialogService { get; init; }

        /// <summary>Liefert die Texte der Dialoge.</summary>
        public required ILocalizationService LocalizationService { get; init; }

        /// <summary>Erkennt das Ordnermuster der Folgen im Bibliotheksordner.</summary>
        public required IEpisodePatternAnalyzer PatternAnalyzer { get; init; }

        /// <summary>Kapselt den Verbindungstest gegen den aktiven Anbieter.</summary>
        public required IConnectionTestCoordinator ConnectionTestCoordinator { get; init; }

        /// <summary>Speichert und liest die Spotify-Zugangsdaten.</summary>
        public required ISpotifyCredentialStore CredentialStore { get; init; }

        /// <summary>Liefert zur Laufzeit die vollständigen Spotify-Einstellungen.</summary>
        public required ISpotifyOptionsProvider OptionsProvider { get; init; }

        /// <summary>Kapselt Datei- und Speicherzugriff der Protokollansicht.</summary>
        public required ILogViewerCoordinator LogViewerCoordinator { get; init; }

        /// <summary>Wird nach dem Speichern mit den neuen Werten versorgt.</summary>
        public required LoggerManager LoggerManager { get; init; }

        /// <summary>Wird über ungespeicherte Änderungen informiert.</summary>
        public required StatusBarViewModel StatusBar { get; init; }

        /// <summary>Kennt die dauerhaft ausgeblendeten Hinweise.</summary>
        public required IDialogSuppressionService SuppressionService { get; init; }

        /// <summary>Protokolliert, was die Schaltflächen der Hinweisliste nebenher tun.</summary>
        public required EchoPlay.Logger.Abstractions.ILoggerFactory LoggerFactory { get; init; }

        /// <summary>Kapselt Ablage, Sprachvorgabe und Neustart. Nullable für Tests.</summary>
        public ILanguageSwitchService? LanguageSwitchService { get; init; }

        /// <summary>Export und Einspielen unter Wartung. Nullable für Tests.</summary>
        public DataTransferViewModel? DataTransfer { get; init; }

        /// <summary>Abgleich zwischen Geräten unter Wartung. Nullable für Tests.</summary>
        public ReplicationSettingsViewModel? Replication { get; init; }
    }
}
