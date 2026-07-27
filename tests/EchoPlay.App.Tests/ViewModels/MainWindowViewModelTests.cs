using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="MainWindowViewModel"/> – es entscheidet, welche Menüpunkte
    /// überhaupt erscheinen. Ein Fehler hier versteckt ganze Programmteile.
    /// </summary>
    public sealed class MainWindowViewModelTests
    {
        private static (MainWindowViewModel ViewModel, FakeNavigationService Navigation) Build(
            AppSettings? settings = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(settings ?? new AppSettings()));

            ServiceProvider provider = services.BuildServiceProvider();
            FakeNavigationService navigation = new();

            return (new MainWindowViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                navigation), navigation);
        }

        [Fact]
        public async Task LoadAsync_WithoutProvider_HidesOnlineLibrary()
        {
            (MainWindowViewModel viewModel, _) = Build(new AppSettings { ActiveProvider = ProviderType.None });

            await viewModel.LoadAsync();

            Assert.False(viewModel.IsMediathekOnlineVisible);
        }

        [Fact]
        public async Task LoadAsync_WithProvider_ShowsOnlineLibrary()
        {
            (MainWindowViewModel viewModel, _) = Build(new AppSettings { ActiveProvider = ProviderType.AppleMusic });

            await viewModel.LoadAsync();

            Assert.True(viewModel.IsMediathekOnlineVisible);
        }

        [Fact]
        public async Task LoadAsync_OnlineOnlyMode_HidesLocalLibraryAndTagManager()
        {
            (MainWindowViewModel viewModel, _) = Build(new AppSettings
            {
                ActiveProvider = ProviderType.AppleMusic,
                OnlineOnlyMode = true
            });

            await viewModel.LoadAsync();

            Assert.False(viewModel.IsMediathekLokalVisible);
            Assert.False(viewModel.IsTagManagerVisible);
        }

        [Theory]
        [InlineData("Startseite", NavigationTarget.Dashboard)]
        [InlineData("MediathekOnline", NavigationTarget.MediathekOnline)]
        [InlineData("MediathekLokal", NavigationTarget.MediathekLokal)]
        [InlineData("TagManager", NavigationTarget.TagManager)]
        [InlineData("Suche", NavigationTarget.Suche)]
        [InlineData("Player", NavigationTarget.Player)]
        [InlineData("Statistik", NavigationTarget.Statistik)]
        [InlineData("Über", NavigationTarget.Über)]
        public void NavigateToMenuTag_KnownTag_NavigatesToTarget(string menuTag, NavigationTarget expected)
        {
            (MainWindowViewModel viewModel, FakeNavigationService navigation) = Build();

            bool handled = viewModel.NavigateToMenuTag(menuTag);

            Assert.True(handled);
            Assert.Equal(expected, Assert.Single(navigation.Navigations).Target);
        }

        [Fact]
        public void NavigateToMenuTag_UnknownTag_DoesNothing()
        {
            (MainWindowViewModel viewModel, FakeNavigationService navigation) = Build();

            bool handled = viewModel.NavigateToMenuTag("GibtEsNicht");

            Assert.False(handled);
            Assert.Empty(navigation.Navigations);
        }

        [Fact]
        public void NavigateToStart_GoesToDashboard()
        {
            (MainWindowViewModel viewModel, FakeNavigationService navigation) = Build();

            viewModel.NavigateToStart();

            Assert.Equal(NavigationTarget.Dashboard, Assert.Single(navigation.Navigations).Target);
        }

        [Fact]
        public void NavigateToSettings_GoesToSettings()
        {
            (MainWindowViewModel viewModel, FakeNavigationService navigation) = Build();

            viewModel.NavigateToSettings();

            Assert.Equal(NavigationTarget.Settings, Assert.Single(navigation.Navigations).Target);
        }
    }
}
