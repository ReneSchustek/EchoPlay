using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="PageModeGuard"/>. Prüft, dass der Guard im Sperrfall
    /// einen Hinweisdialog zeigt und über den NavigationService zurücknavigiert,
    /// im erlaubten Modus dagegen still durchlässt.
    /// </summary>
    public sealed class PageModeGuardTests
    {
        private static (PageModeGuard guard,
                        FakeAppSettingsDataService settings,
                        FakeErrorDialogService errorDialog,
                        FakeNavigationService navigation) BuildGuard(AppSettings initialSettings)
        {
            FakeAppSettingsDataService settings = new(initialSettings);

            ServiceCollection services = new();
            services.AddScoped<IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();

            FakeErrorDialogService errorDialog = new();
            FakeNavigationService navigation = new();

            PageModeGuard guard = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                errorDialog,
                new FakeLocalizationService(),
                navigation);

            return (guard, settings, errorDialog, navigation);
        }

        [Fact]
        public async Task EnsureOnlineAccessAsync_ReturnsTrue_WhenOnline()
        {
            (PageModeGuard guard, _, FakeErrorDialogService errorDialog, FakeNavigationService navigation) =
                BuildGuard(new AppSettings { OfflineMode = false });

            bool result = await guard.EnsureOnlineAccessAsync();

            Assert.True(result);
            Assert.Empty(errorDialog.ShownDialogs);
            Assert.Equal(0, navigation.GoBackCallCount);
        }

        [Fact]
        public async Task EnsureOnlineAccessAsync_ReturnsFalseAndGoesBack_WhenOffline()
        {
            (PageModeGuard guard, _, FakeErrorDialogService errorDialog, FakeNavigationService navigation) =
                BuildGuard(new AppSettings { OfflineMode = true });

            bool result = await guard.EnsureOnlineAccessAsync();

            Assert.False(result);
            Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("OfflineModeSearchHintTitle", errorDialog.ShownDialogs[0].Title);
            Assert.Equal("OfflineModeSearchHintMessage", errorDialog.ShownDialogs[0].Message);
            Assert.Equal(1, navigation.GoBackCallCount);
        }

        [Fact]
        public async Task EnsureLocalAccessAsync_ReturnsTrue_WhenLocalAllowed()
        {
            (PageModeGuard guard, _, FakeErrorDialogService errorDialog, FakeNavigationService navigation) =
                BuildGuard(new AppSettings { OnlineOnlyMode = false });

            bool result = await guard.EnsureLocalAccessAsync();

            Assert.True(result);
            Assert.Empty(errorDialog.ShownDialogs);
            Assert.Equal(0, navigation.GoBackCallCount);
        }

        [Fact]
        public async Task EnsureLocalAccessAsync_ReturnsFalseAndGoesBack_WhenOnlineOnly()
        {
            (PageModeGuard guard, _, FakeErrorDialogService errorDialog, FakeNavigationService navigation) =
                BuildGuard(new AppSettings { OnlineOnlyMode = true });

            bool result = await guard.EnsureLocalAccessAsync();

            Assert.False(result);
            Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("OnlineOnlyModeHintTitle", errorDialog.ShownDialogs[0].Title);
            Assert.Equal("OnlineOnlyModeHintMessage", errorDialog.ShownDialogs[0].Message);
            Assert.Equal(1, navigation.GoBackCallCount);
        }
    }
}
