using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="LanguageSwitchService"/>.
    /// Der Neustart läuft ausschließlich über <see cref="FakeProcessLauncher"/> — es wird nie
    /// ein echter Prozess gestartet. Genau daran scheiterte ein früherer Versuch: der Testhost
    /// startete sich selbst und vervielfältigte sich (siehe Arbeitspaket 437).
    /// </summary>
    public sealed class LanguageSwitchServiceTests
    {
        private static (LanguageSwitchService Service, FakeAppSettingsDataService Settings, FakeProcessLauncher Launcher)
            Build(string? executablePath = @"C:\Programs\EchoPlay\EchoPlay.App.exe", bool startSucceeds = true)
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveLanguage = "de" });
            FakeProcessLauncher launcher = new(executablePath, startSucceeds);

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();

            LanguageSwitchService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                launcher,
                new FakeLoggerFactory());

            return (service, settings, launcher);
        }

        [Fact]
        public async Task ChangeLanguageAsync_PersistsLanguage()
        {
            (LanguageSwitchService service, FakeAppSettingsDataService settings, _) = Build();

            _ = await service.ChangeLanguageAsync("en", CancellationToken.None);

            AppSettings after = await settings.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("en", after.ActiveLanguage);
        }

        [Fact]
        public async Task ChangeLanguageAsync_AsksLauncherForRestart()
        {
            (LanguageSwitchService service, _, FakeProcessLauncher launcher) = Build();

            _ = await service.ChangeLanguageAsync("en", CancellationToken.None);

            string path = Assert.Single(launcher.StartedPaths);
            Assert.EndsWith("EchoPlay.App.exe", path, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task ChangeLanguageAsync_UnknownExecutablePath_SkipsRestartButKeepsLanguage()
        {
            (LanguageSwitchService service, FakeAppSettingsDataService settings, FakeProcessLauncher launcher) =
                Build(executablePath: null);

            bool restarted = await service.ChangeLanguageAsync("en", CancellationToken.None);

            Assert.False(restarted);
            Assert.Empty(launcher.StartedPaths);
            AppSettings after = await settings.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("en", after.ActiveLanguage);
        }

        [Fact]
        public async Task ChangeLanguageAsync_LauncherRefuses_ReportsFailure()
        {
            // Der echte Launcher verweigert den Start, wenn der Prozess nicht die App ist.
            // Der Aufrufer muss das erfahren, um den Nutzer um einen manuellen Neustart zu bitten.
            (LanguageSwitchService service, _, _) = Build(startSucceeds: false);

            bool restarted = await service.ChangeLanguageAsync("en", CancellationToken.None);

            Assert.False(restarted);
        }

        [Fact]
        public async Task ChangeLanguageAsync_EmptyCode_DoesNothing()
        {
            (LanguageSwitchService service, FakeAppSettingsDataService settings, FakeProcessLauncher launcher) = Build();

            bool result = await service.ChangeLanguageAsync("   ", CancellationToken.None);

            Assert.False(result);
            Assert.Empty(launcher.StartedPaths);
            AppSettings after = await settings.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("de", after.ActiveLanguage);
        }

        [Fact]
        public void ApplyOverride_EmptyCode_ReturnsFalse()
        {
            (LanguageSwitchService service, _, _) = Build();

            Assert.False(service.ApplyOverride(""));
        }
    }
}
