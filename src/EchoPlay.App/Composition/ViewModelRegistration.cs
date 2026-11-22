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
                new SearchResultServices
                {
                    ImportService = provider.GetRequiredService<ImportService>(),
                    ErrorDialogService = provider.GetRequiredService<IErrorDialogService>(),
                    LocalizationService = provider.GetRequiredService<ILocalizationService>(),
                    BackgroundCoverService = provider.GetRequiredService<BackgroundCoverService>(),
                },
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<IPageModeGuard>(),
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
                new DashboardViewModelContext
                {
                    ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
                    ErrorDialogService = provider.GetRequiredService<IErrorDialogService>(),
                    ConfirmationDialogService = provider.GetRequiredService<IConfirmationDialogService>(),
                    PlayerService = provider.GetRequiredService<IPlayerService>(),
                    LoggerFactory = provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                    CoverService = provider.GetRequiredService<ICoverService>(),
                    LocalizationService = provider.GetRequiredService<ILocalizationService>(),
                    Clock = provider.GetRequiredService<IClock>(),
                    BackgroundCoverService = provider.GetRequiredService<BackgroundCoverService>(),
                    NewReleaseEventService = provider.GetRequiredService<INewReleaseEventService>(),
                    ReleaseCalendarService = provider.GetRequiredService<ReleaseCalendarRefreshService>(),
                });


        /// <summary>Baut das Ansichtsmodell aus dem Container.</summary>
        private static OnlineLibraryViewModel CreateOnlineLibraryViewModel(IServiceProvider provider) => new(
                new OnlineLibraryActionsContext
                {
                    ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
                    ConfirmationDialogService = provider.GetRequiredService<IConfirmationDialogService>(),
                    ImportService = provider.GetRequiredService<ImportService>(),
                    ErrorDialogService = provider.GetRequiredService<IErrorDialogService>(),
                    LocalizationService = provider.GetRequiredService<ILocalizationService>(),
                    OnlineAccessGuard = provider.GetRequiredService<IOnlineAccessGuard>(),
                    CoverDownloader = provider.GetRequiredService<ICoverDownloader>(),
                    CoverCacheService = provider.GetRequiredService<EpisodeCoverCacheService>(),
                    CoverService = provider.GetRequiredService<ICoverService>(),
                    BackgroundCoverService = provider.GetRequiredService<BackgroundCoverService>(),
                    WatchToggleService = provider.GetRequiredService<IWatchToggleService>(),
                    RateLimiter = provider.GetRequiredService<IHostRateLimiter>(),
                },
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>(),
                provider.GetRequiredService<INavigationService>());


        /// <summary>Baut das Ansichtsmodell aus dem Container.</summary>
        private static LocalLibraryViewModel CreateLocalLibraryViewModel(IServiceProvider provider) => new(
                new LocalLibraryViewModelContext
                {
                    ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
                    SyncService = provider.GetRequiredService<ISyncService>(),
                    PlayerService = provider.GetRequiredService<IPlayerService>(),
                    ErrorDialogService = provider.GetRequiredService<IErrorDialogService>(),
                    ConfirmationDialogService = provider.GetRequiredService<IConfirmationDialogService>(),
                    StatusBar = provider.GetRequiredService<StatusBarViewModel>(),
                    CoverLoader = provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>(),
                    ScanEventService = provider.GetRequiredService<IScanEventService>(),
                    OnlineAccessGuard = provider.GetRequiredService<IOnlineAccessGuard>(),
                    Clock = provider.GetRequiredService<IClock>(),
                    CoverService = provider.GetRequiredService<ICoverService>(),
                    WatchToggleService = provider.GetRequiredService<IWatchToggleService>(),
                    PageModeGuard = provider.GetRequiredService<IPageModeGuard>(),
                    RestructureCoordinator = provider.GetRequiredService<IFolderRestructureCoordinator>(),
                    MissingEpisodesCoordinator = provider.GetRequiredService<IMissingEpisodesCoordinator>(),
                    CoverCoordinator = provider.GetRequiredService<IEpisodeCoverCoordinator>(),
                    Logger = provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>().CreateLogger("LocalLibrary"),
                    FilterState = provider.GetRequiredService<FilterStateStore>(),
                    FolderRenameCoordinator = provider.GetRequiredService<IEpisodeFolderRenameCoordinator>(),
                    MetadataEditor = provider.GetRequiredService<ILocalMetadataEditor>(),
                });

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
            new SettingsViewModelContext
            {
                ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
                ThemeService = provider.GetRequiredService<IThemeService>(),
                SyncService = provider.GetRequiredService<ISyncService>(),
                ErrorDialogService = provider.GetRequiredService<IErrorDialogService>(),
                ConfirmationDialogService = provider.GetRequiredService<IConfirmationDialogService>(),
                LocalizationService = provider.GetRequiredService<ILocalizationService>(),
                PatternAnalyzer = provider.GetRequiredService<EchoPlay.LocalLibrary.Analysis.IEpisodePatternAnalyzer>(),
                ConnectionTestCoordinator = provider.GetRequiredService<IConnectionTestCoordinator>(),
                CredentialStore = provider.GetRequiredService<ISpotifyCredentialStore>(),
                OptionsProvider = provider.GetRequiredService<ISpotifyOptionsProvider>(),
                LogViewerCoordinator = provider.GetRequiredService<ILogViewerCoordinator>(),
                LoggerManager = provider.GetRequiredService<EchoPlay.Logger.Core.LoggerManager>(),
                StatusBar = provider.GetRequiredService<StatusBarViewModel>(),
                SuppressionService = provider.GetRequiredService<IDialogSuppressionService>(),
                LoggerFactory = provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                LanguageSwitchService = provider.GetRequiredService<ILanguageSwitchService>(),
                DataTransfer = CreateDataTransferViewModel(provider),
                Replication = CreateReplicationSettingsViewModel(provider),
            });

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
