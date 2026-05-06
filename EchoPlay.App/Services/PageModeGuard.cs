using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IPageModeGuard"/>.
    /// Singleton-Service: nutzt einen eigenen DI-Scope pro Aufruf, weil
    /// <see cref="IAppSettingsDataService"/> Scoped ist.
    /// </summary>

    public sealed class PageModeGuard : IPageModeGuard
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly INavigationService _navigationService;

        /// <summary>
        /// Initialisiert den Guard mit den benötigten Diensten.
        /// </summary>
        /// <param name="scopeFactory">Liefert pro Prüfung einen frischen Scope für den AppSettings-Zugriff.</param>
        /// <param name="errorDialogService">Zeigt den Hinweisdialog im Sperrfall.</param>
        /// <param name="localizationService">Liefert Titel und Text des Hinweisdialogs lokalisiert.</param>
        /// <param name="navigationService">Löst die Rücknavigation aus, wenn die Page nicht angezeigt werden darf.</param>

        public PageModeGuard(
            IServiceScopeFactory scopeFactory,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            INavigationService navigationService)
        {
            _scopeFactory = scopeFactory;
            _errorDialogService = errorDialogService;
            _localizationService = localizationService;
            _navigationService = navigationService;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> EnsureOnlineAccessAsync(CancellationToken cancellationToken = default)
        {
            AppSettings settings = await LoadSettingsAsync(cancellationToken);

            if (!settings.OfflineMode)
            {
                return true;
            }

            await _errorDialogService.ShowAsync(_localizationService.Get("OfflineModeSearchHintTitle"), _localizationService.Get("OfflineModeSearchHintMessage"), cancellationToken);
            _ = _navigationService.GoBack();
            return false;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> EnsureLocalAccessAsync(CancellationToken cancellationToken = default)
        {
            AppSettings settings = await LoadSettingsAsync(cancellationToken);

            if (!settings.OnlineOnlyMode)
            {
                return true;
            }

            await _errorDialogService.ShowAsync(_localizationService.Get("OnlineOnlyModeHintTitle"), _localizationService.Get("OnlineOnlyModeHintMessage"), cancellationToken);
            _ = _navigationService.GoBack();
            return false;
        }

        /// <summary>
        /// Lädt die aktuellen <see cref="AppSettings"/> aus einem frischen Scope.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        private async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            return await settingsService.GetAsync(cancellationToken);
        }
    }
}
