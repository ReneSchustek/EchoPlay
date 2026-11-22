using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert die Ansichtsmodelle. Sie sind bis auf die beiden fensterweiten
    /// Ausnahmen kurzlebig: Jede Navigation erzeugt eine frische Instanz, damit kein
    /// Zustand einer verlassenen Seite in die nächste durchschlägt.
    /// </summary>
    internal static class ViewModelRegistration
    {
        /// <summary>
        /// Registriert alle Ansichtsmodelle der Anwendung.
        /// </summary>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayViewModels(this IServiceCollection services)
        {
            AddWindowScopedViewModels(services);
            AddLibraryViewModels(services);
            AddPlaybackViewModels(services);
            AddToolViewModels(services);
            return services;
        }

        /// <summary>
        /// Statusleiste und Hauptfenster gibt es genau einmal. Die Statistiken der
        /// Statusleiste müssen anwendungsweit denselben Stand zeigen.
        /// </summary>
        private static void AddWindowScopedViewModels(IServiceCollection services)
        {
            _ = services.AddSingleton<StatusBarViewModel>(provider => new StatusBarViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IThemeService>(),
                provider.GetRequiredService<TaskbarProgressService>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ILanguageSwitchService>(),
                provider.GetRequiredService<IErrorDialogService>()));

            _ = services.AddSingleton<MainWindowViewModel>();

            // Der Hinweis zum Abgleich in der Statusleiste; das Fenster erzeugt ihn auf seinem
            // Thread, daher die Warteschlange des aufrufenden Threads.
            _ = services.AddSingleton(provider => new ReplicationStatusViewModel(
                provider.GetRequiredService<DeviceReplicationService>(),
                provider.GetRequiredService<ILocalizationService>(),
                EchoPlay.App.Infrastructure.UiDispatcher.TryGetForCurrentThread()));
        }

        /// <summary>Startseite, Mediatheken und Suche.</summary>
        private static void AddLibraryViewModels(IServiceCollection services)
        {
            _ = services.AddTransient(CreateDashboardViewModel);
            _ = services.AddTransient(CreateOnlineLibraryViewModel);
            _ = services.AddTransient(CreateLocalLibraryViewModel);

            _ = services.AddTransient<SearchViewModel>(provider => new SearchViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<BackgroundCoverService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>().CreateLogger("SearchViewModel")));

            _ = services.AddTransient<SeriesDetailViewModel>(provider => new SeriesDetailViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IPlayerService>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ICoverService>(),
                provider.GetRequiredService<BackgroundCoverService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>()));
        }

        /// <summary>Baut das Ansichtsmodell aus dem Container.</summary>
        private static DashboardViewModel CreateDashboardViewModel(IServiceProvider provider) => new(
                new DashboardViewModelContext(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetRequiredService<IErrorDialogService>(),
                    provider.GetRequiredService<IConfirmationDialogService>(),
                    provider.GetRequiredService<IPlayerService>(),
                    provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                    provider.GetRequiredService<ICoverService>(),
                    provider.GetRequiredService<ILocalizationService>(),
                    provider.GetRequiredService<IClock>(),
                    provider.GetRequiredService<BackgroundCoverService>(),
                    provider.GetRequiredService<INewReleaseEventService>(),
                    provider.GetRequiredService<ReleaseCalendarRefreshService>()));


        /// <summary>Baut das Ansichtsmodell aus dem Container.</summary>
        private static OnlineLibraryViewModel CreateOnlineLibraryViewModel(IServiceProvider provider) => new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<ICoverDownloader>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<ICoverService>(),
                provider.GetRequiredService<BackgroundCoverService>(),
                provider.GetRequiredService<IWatchToggleService>(),
                provider.GetRequiredService<IHostRateLimiter>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>(),
                provider.GetRequiredService<INavigationService>());


        /// <summary>Baut das Ansichtsmodell aus dem Container.</summary>
        private static LocalLibraryViewModel CreateLocalLibraryViewModel(IServiceProvider provider) => new(
                new LocalLibraryViewModelContext(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetRequiredService<ISyncService>(),
                    provider.GetRequiredService<IPlayerService>(),
                    provider.GetRequiredService<IErrorDialogService>(),
                    provider.GetRequiredService<IConfirmationDialogService>(),
                    provider.GetRequiredService<StatusBarViewModel>(),
                    provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>(),
                    provider.GetRequiredService<IScanEventService>(),
                    provider.GetRequiredService<IOnlineAccessGuard>(),
                    provider.GetRequiredService<IClock>(),
                    provider.GetRequiredService<ICoverService>(),
                    provider.GetRequiredService<IWatchToggleService>(),
                    provider.GetRequiredService<IPageModeGuard>(),
                    provider.GetRequiredService<IFolderRestructureCoordinator>(),
                    provider.GetRequiredService<IMissingEpisodesCoordinator>(),
                    provider.GetRequiredService<IEpisodeCoverCoordinator>(),
                    provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>().CreateLogger("LocalLibrary"),
                    provider.GetRequiredService<FilterStateStore>(),
                    provider.GetRequiredService<IEpisodeFolderRenameCoordinator>(),
                    provider.GetRequiredService<ILocalMetadataEditor>()));

        /// <summary>Wiedergabeseite und der kleine Spieler in der Kopfzeile.</summary>
        private static void AddPlaybackViewModels(IServiceCollection services)
        {
            _ = services.AddTransient<PlayerViewModel>();
            _ = services.AddTransient<MiniPlayerViewModel>();
        }

        /// <summary>Einstellungen, Import, Tag-Manager, Statistik und Angekündigt.</summary>
        private static void AddToolViewModels(IServiceCollection services)
        {
            _ = services.AddTransient<SettingsViewModel>(CreateSettingsViewModel);

            _ = services.AddTransient<ImportViewModel>(provider => new ImportViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<StatusBarViewModel>()));

            _ = services.AddTransient<TagManagerViewModel>();
            _ = services.AddTransient<StatistikViewModel>();
            _ = services.AddTransient<AnnouncedViewModel>(provider => new AnnouncedViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ILocalizationService>()));
        }

        /// <summary>Die Einstellungsseite samt Datenumzug und Abgleich.</summary>
        private static SettingsViewModel CreateSettingsViewModel(IServiceProvider provider) => new(
            new SettingsViewModelContext(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IThemeService>(),
                provider.GetRequiredService<ISyncService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Analysis.IEpisodePatternAnalyzer>(),
                provider.GetRequiredService<IConnectionTestCoordinator>(),
                provider.GetRequiredService<ISpotifyCredentialStore>(),
                provider.GetRequiredService<ISpotifyOptionsProvider>(),
                provider.GetRequiredService<ILogViewerCoordinator>(),
                provider.GetRequiredService<EchoPlay.Logger.Core.LoggerManager>(),
                provider.GetRequiredService<StatusBarViewModel>(),
                provider.GetRequiredService<IDialogSuppressionService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                provider.GetRequiredService<ILanguageSwitchService>(),
                CreateDataTransferViewModel(provider),
                CreateReplicationSettingsViewModel(provider)));

        /// <summary>Datenumzug und Mediathek-Archiv der Einstellungsseite.</summary>
        private static DataTransferViewModel CreateDataTransferViewModel(IServiceProvider provider) => new(
            provider.GetRequiredService<IDataTransferService>(),
            provider.GetRequiredService<ILibraryTransferService>(),
            provider.GetRequiredService<ISpotifyCredentialStore>(),
            provider.GetRequiredService<IProcessLauncher>(),
            provider.GetRequiredService<IAppInstanceRegistration>(),
            provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

        /// <summary>Abgleich über einen gemeinsamen Ordner auf der Einstellungsseite.</summary>
        private static ReplicationSettingsViewModel CreateReplicationSettingsViewModel(IServiceProvider provider) => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<DeviceReplicationService>(),
            provider.GetRequiredService<EchoPlay.Core.Replication.IDeviceIdentity>(),
            provider.GetRequiredService<IConfirmationDialogService>(),
            provider.GetRequiredService<ILocalizationService>(),
            provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());
    }
}
