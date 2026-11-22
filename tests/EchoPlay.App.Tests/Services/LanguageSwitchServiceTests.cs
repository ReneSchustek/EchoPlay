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
    /// Der Neustart läuft ausschließlich über <see cref="FakeProcessLauncher"/>, es wird nie
    /// ein echter Prozess gestartet. Sonst startet sich der Testhost selbst und
    /// vervielfältigt sich.
    /// </summary>
    public sealed class LanguageSwitchServiceTests
    {
        private static (LanguageSwitchService Service, FakeAppSettingsDataService Settings, FakeProcessLauncher Launcher)
            Build(
                string? executablePath = @"C:\Programs\EchoPlay\EchoPlay.App.exe",
                bool startSucceeds = true,
                FakeAppInstanceRegistration? registration = null)
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveLanguage = "de" });
            FakeProcessLauncher launcher = new(executablePath, startSucceeds);

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();

            LanguageSwitchService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                launcher,
                registration ?? new FakeAppInstanceRegistration(),
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
        public async Task ChangeLanguageAsync_ReleasesTheInstanceBeforeStartingTheSuccessor()
        {
            // Hielte dieser Prozess den Anspruch noch, gäbe der Nachfolger an ihn ab und
            // endete sofort. Nach dem Beenden hier liefe dann gar keine Instanz mehr.
            FakeAppInstanceRegistration registration = new();
            (LanguageSwitchService service, _, FakeProcessLauncher launcher) = Build(registration: registration);
            int startsBeforeRelease = -1;
            registration.OnRelease = () => startsBeforeRelease = launcher.StartedPaths.Count;

            _ = await service.ChangeLanguageAsync("en", CancellationToken.None);

            Assert.Equal(["Release"], registration.Calls);
            Assert.Equal(0, startsBeforeRelease);
            _ = Assert.Single(launcher.StartedPaths);
        }

        [Fact]
        public async Task ChangeLanguageAsync_LauncherRefuses_ReclaimsTheInstance()
        {
            // Der Prozess bleibt offen, also muss er weiter die laufende Instanz sein,
            // sonst liefe ab jetzt jeder weitere Start nebenher.
            FakeAppInstanceRegistration registration = new();
            (LanguageSwitchService service, _, _) = Build(startSucceeds: false, registration: registration);

            _ = await service.ChangeLanguageAsync("en", CancellationToken.None);

            Assert.Equal(["Release", "Reclaim"], registration.Calls);
        }

        [Fact]
        public async Task ChangeLanguageAsync_UnknownExecutablePath_KeepsTheInstance()
        {
            FakeAppInstanceRegistration registration = new();
            (LanguageSwitchService service, _, _) = Build(executablePath: null, registration: registration);

            _ = await service.ChangeLanguageAsync("en", CancellationToken.None);

            Assert.Empty(registration.Calls);
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
