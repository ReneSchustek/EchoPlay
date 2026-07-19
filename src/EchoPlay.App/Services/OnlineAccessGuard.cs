using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Implementierung von <see cref="IOnlineAccessGuard"/>.
    /// Prüft den Offline-Modus über <see cref="IAppSettingsDataService"/>, zeigt bei Bedarf
    /// einen Bestätigungsdialog und schaltet die <see cref="StatusBarViewModel"/> temporär auf "Online".
    /// </summary>

    public sealed class OnlineAccessGuard : IOnlineAccessGuard
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialog;
        private readonly StatusBarViewModel _statusBar;

        /// <summary>
        /// Initialisiert den Guard mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für den Zugriff auf AppSettings.</param>
        /// <param name="confirmationDialog">Service für Ja/Abbrechen-Dialoge.</param>
        /// <param name="statusBar">StatusBar-ViewModel für die temporäre Online-Anzeige.</param>

        public OnlineAccessGuard(
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialog,
            StatusBarViewModel statusBar)
        {
            _scopeFactory = scopeFactory;
            _confirmationDialog = confirmationDialog;
            _statusBar = statusBar;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<IDisposable?> RequestOnlineAccessAsync(CancellationToken cancellationToken = default)
        {
            // Im Online-Modus: kein Dialog, kein temporärer Status nötig
            bool isOffline = await IsOfflineModeActiveAsync(cancellationToken);
            if (!isOffline)
            {
                return NoOpDisposable.Instance;
            }

            // Offline-Modus aktiv → Nutzer fragen, ob die Aktion trotzdem ausgeführt werden soll
            ResourceLoader resources = ResourceLoader.GetForViewIndependentUse();
            string title = resources.GetString("OfflineOnlineAccessTitle");
            string message = resources.GetString("OfflineOnlineAccessMessage");

            bool confirmed = await _confirmationDialog.ConfirmAsync(title, message, cancellationToken);
            if (!confirmed)
            {
                return null;
            }

            // Nutzer hat bestätigt → StatusBar temporär auf "Online" umschalten
            _statusBar.IsTemporarilyOnline = true;
            return new TemporaryOnlineScope(_statusBar);
        }

        /// <summary>
        /// Liest den aktuellen Offline-Modus aus den AppSettings.
        /// Eigener Scope, weil der Guard als Singleton registriert ist.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        private async Task<bool> IsOfflineModeActiveAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider
                .GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            return settings.OfflineMode;
        }

        /// <summary>
        /// Setzt den temporären Online-Status beim Dispose zurück auf Offline.
        /// Das using-Pattern garantiert, dass der Status auch bei Exceptions zurückgesetzt wird.
        /// </summary>

        private sealed class TemporaryOnlineScope : IDisposable
        {
            private readonly StatusBarViewModel _statusBar;

            public TemporaryOnlineScope(StatusBarViewModel statusBar)
            {
                _statusBar = statusBar;
            }

            public void Dispose()
            {
                _statusBar.IsTemporarilyOnline = false;
            }
        }

        /// <summary>
        /// No-Op-Disposable für den Online-Modus – Dispose tut nichts.
        /// Singleton, da zustandslos.
        /// </summary>

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
