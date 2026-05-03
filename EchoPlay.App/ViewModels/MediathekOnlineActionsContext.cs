using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Service-Abhängigkeiten für <see cref="MediathekOnlineActions"/>.
    /// Sub-VMs und Zustands-Callbacks bleiben separate Konstruktorparameter.
    /// </summary>
    internal sealed record MediathekOnlineActionsContext(
        IServiceScopeFactory ScopeFactory,
        IConfirmationDialogService ConfirmationDialogService,
        ImportService ImportService,
        IErrorDialogService ErrorDialogService,
        ILocalizationService LocalizationService,
        IOnlineAccessGuard OnlineAccessGuard,
        EpisodeCoverCacheService? CoverCacheService,
        CoverService CoverService,
        BackgroundCoverService? BackgroundCoverService,
        IWatchToggleService? WatchToggleService,
        IHttpClientFactory HttpClientFactory,
        IHostRateLimiter? RateLimiter);
}
