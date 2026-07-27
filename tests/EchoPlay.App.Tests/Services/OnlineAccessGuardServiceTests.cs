using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="OnlineAccessGuard"/> – die Stelle, an der EchoPlay im
    /// Offline-Modus nachfragt, bevor es doch ins Netz geht. Lehnt der Nutzer ab, muss die
    /// aufrufende Aktion sicher erkennen, dass sie nicht loslaufen darf.
    /// </summary>
    public sealed class OnlineAccessGuardServiceTests
    {
        private static (OnlineAccessGuard Guard, StatusBarViewModel StatusBar, FakeConfirmationDialogService Dialog)
            Build(bool offlineMode, bool confirmed)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(new AppSettings { OfflineMode = offlineMode }));
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());

            FakeConfirmationDialogService dialog = new(confirmed);

            return (new OnlineAccessGuard(scopeFactory, dialog, statusBar), statusBar, dialog);
        }

        [Fact]
        public async Task RequestOnlineAccessAsync_OnlineMode_AsksNothing()
        {
            (OnlineAccessGuard guard, StatusBarViewModel statusBar, FakeConfirmationDialogService dialog) =
                Build(offlineMode: false, confirmed: true);

            using IDisposable? scope = await guard.RequestOnlineAccessAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(scope);
            Assert.Equal(0, dialog.CallCount);
            Assert.False(statusBar.IsTemporarilyOnline);
        }

        [Fact]
        public async Task RequestOnlineAccessAsync_OfflineModeConfirmed_SwitchesStatusTemporarily()
        {
            (OnlineAccessGuard guard, StatusBarViewModel statusBar, FakeConfirmationDialogService dialog) =
                Build(offlineMode: true, confirmed: true);

            IDisposable? scope = await guard.RequestOnlineAccessAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(scope);
            Assert.Equal(1, dialog.CallCount);
            Assert.True(statusBar.IsTemporarilyOnline);

            // Erst das Freigeben beendet den temporären Status – sonst bliebe die
            // Statusleiste dauerhaft auf „Online", obwohl der Offline-Modus aktiv ist.
            scope!.Dispose();
            Assert.False(statusBar.IsTemporarilyOnline);
        }

        [Fact]
        public async Task RequestOnlineAccessAsync_OfflineModeDeclined_ReturnsNull()
        {
            (OnlineAccessGuard guard, StatusBarViewModel statusBar, FakeConfirmationDialogService dialog) =
                Build(offlineMode: true, confirmed: false);

            IDisposable? scope = await guard.RequestOnlineAccessAsync(TestContext.Current.CancellationToken);

            Assert.Null(scope);
            Assert.Equal(1, dialog.CallCount);
            Assert.False(statusBar.IsTemporarilyOnline);
        }
    }
}
