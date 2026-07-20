using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Service-Abhängigkeiten für <see cref="MediathekLokalViewModel"/>.
    /// Sub-VMs werden intern erzeugt und sind nicht Teil des Context-Records.
    /// </summary>
    internal sealed record MediathekLokalViewModelContext(
        IServiceScopeFactory ScopeFactory,
        ISyncService SyncService,
        IPlayerService PlayerService,
        IErrorDialogService ErrorDialogService,
        IConfirmationDialogService ConfirmationDialogService,
        StatusBarViewModel StatusBar,
        ILocalCoverLoader CoverLoader,
        IScanEventService ScanEventService,
        ICoverSearchService CoverSearchService,
        IOnlineAccessGuard OnlineAccessGuard,
        IOnlineEpisodeChecker OnlineEpisodeChecker,
        IClock Clock,
        EchoPlay.App.Services.ICoverService? CoverService = null,
        IWatchToggleService? WatchToggleService = null,
        IPageModeGuard? PageModeGuard = null,
        IFolderRestructureCoordinator? RestructureCoordinator = null,
        IMissingEpisodesCoordinator? MissingEpisodesCoordinator = null,
        IEpisodeCoverCoordinator? CoverCoordinator = null,
        ILogger? Logger = null);
}
