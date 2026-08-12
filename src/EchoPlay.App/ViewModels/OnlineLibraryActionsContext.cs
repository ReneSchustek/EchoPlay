using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Service-Abhängigkeiten für <see cref="OnlineLibraryActions"/>.
    /// Sub-VMs und Zustands-Callbacks bleiben separate Konstruktorparameter.
    /// </summary>
    internal sealed record OnlineLibraryActionsContext(
        IServiceScopeFactory ScopeFactory,
        IConfirmationDialogService ConfirmationDialogService,
        ImportService ImportService,
        IErrorDialogService ErrorDialogService,
        ILocalizationService LocalizationService,
        IOnlineAccessGuard OnlineAccessGuard,
        EpisodeCoverCacheService? CoverCacheService,
        ICoverService CoverService,
        BackgroundCoverService? BackgroundCoverService,
        IWatchToggleService? WatchToggleService,
        ICoverDownloader CoverDownloader,
        IHostRateLimiter? RateLimiter);
}
