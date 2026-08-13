using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Dienste für <see cref="SettingsViewModel"/>.
    /// Dieselbe Form wie <see cref="LocalLibraryViewModelContext"/>: Vierzehn einzelne
    /// Konstruktor-Parameter waren an der Aufrufstelle nicht mehr auseinanderzuhalten —
    /// zwei vertauschte Dienste gleichen Typs hätte kein Übersetzer bemerkt.
    /// Die Reiter-Ansichtsmodelle entstehen intern und gehören nicht hierher.
    /// </summary>
    /// <param name="ScopeFactory">Für datenbanknahe Zugriffe beim Laden und Speichern.</param>
    /// <param name="ThemeService">Wendet ein Farbschema sofort an.</param>
    /// <param name="SyncService">Gleicht die lokale Mediathek ab.</param>
    /// <param name="ErrorDialogService">Zeigt Fehlerhinweise.</param>
    /// <param name="ConfirmationDialogService">Fragt beim Verlassen mit ungespeicherten Änderungen nach.</param>
    /// <param name="LocalizationService">Liefert die Texte der Dialoge.</param>
    /// <param name="PatternAnalyzer">Erkennt das Ordnermuster der Folgen im Bibliotheksordner.</param>
    /// <param name="ConnectionTestCoordinator">Kapselt den Verbindungstest gegen den aktiven Anbieter.</param>
    /// <param name="CredentialStore">Speichert und liest die Spotify-Zugangsdaten.</param>
    /// <param name="OptionsProvider">Liefert zur Laufzeit die vollständigen Spotify-Einstellungen.</param>
    /// <param name="LogViewerCoordinator">Kapselt Datei- und Speicherzugriff der Protokollansicht.</param>
    /// <param name="LoggerManager">Wird nach dem Speichern mit den neuen Werten versorgt.</param>
    /// <param name="StatusBar">Wird über ungespeicherte Änderungen informiert.</param>
    /// <param name="LanguageSwitchService">Kapselt Ablage, Sprachvorgabe und Neustart. Nullable für Tests.</param>
    internal sealed record SettingsViewModelContext(
        IServiceScopeFactory ScopeFactory,
        IThemeService ThemeService,
        ISyncService SyncService,
        IErrorDialogService ErrorDialogService,
        IConfirmationDialogService ConfirmationDialogService,
        ILocalizationService LocalizationService,
        IEpisodePatternAnalyzer PatternAnalyzer,
        IConnectionTestCoordinator ConnectionTestCoordinator,
        ISpotifyCredentialStore CredentialStore,
        ISpotifyOptionsProvider OptionsProvider,
        ILogViewerCoordinator LogViewerCoordinator,
        LoggerManager LoggerManager,
        StatusBarViewModel StatusBar,
        ILanguageSwitchService? LanguageSwitchService = null);
}
