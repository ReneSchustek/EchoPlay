using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für das Verlassen der Einstellungsseite und den Sprachwechsel.
    /// Die Load-/Save-Wege der Tabs stehen in <see cref="SettingsViewModelTests"/>.
    /// </summary>
    public sealed class SettingsLeaveAndLanguageTests
    {
        private static LoggerManager BuildLoggerManager()
        {
            LoggerOptions options = new() { EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory loggerFactory = new([], options);
            LogCleanupService cleanup = new(options);
            return new LoggerManager(loggerFactory, cleanup, options);
        }

        private static SettingsViewModel BuildViewModel(
            FakeAppSettingsDataService settingsService,
            FakeConfirmationDialogService? confirmationDialogService = null,
            FakeErrorDialogService? errorDialogService = null,
            FakeLanguageSwitchService? languageSwitchService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IDatabaseMaintenanceService>(_ => new FakeDatabaseMaintenanceService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            FakeSpotifyCredentialStore credentialStore = new();
            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());

            return new SettingsViewModel(new SettingsViewModelContext(
                scopeFactory,
                new FakeThemeService(),
                new FakeSyncService(new SyncResult()),
                errorDialogService ?? new FakeErrorDialogService(),
                confirmationDialogService ?? new FakeConfirmationDialogService(),
                new FakeLocalizationService(),
                new FakeEpisodePatternAnalyzer(),
                new FakeConnectionTestCoordinator(),
                credentialStore,
                new FakeSpotifyOptionsProvider(credentialStore),
                new FakeLogViewerCoordinator(),
                BuildLoggerManager(),
                statusBar,
                new FakeDialogSuppressionService(),
                new FakeLoggerFactory(),
                languageSwitchService));
        }

        [Fact]
        public async Task Verlassen_OhneAenderungFragtNicht()
        {
            FakeConfirmationDialogService confirmation = new();
            SettingsViewModel sut = BuildViewModel(new FakeAppSettingsDataService(), confirmation);
            await sut.LoadAsync();

            bool mayLeave = await sut.CanLeaveAsync();

            Assert.True(mayLeave);
            Assert.Equal(0, confirmation.CallCount);
        }

        [Fact]
        public async Task Verlassen_MitAenderungSpeichertNachZustimmung()
        {
            FakeAppSettingsDataService settingsService = new();
            FakeConfirmationDialogService confirmation = new(result: true);
            SettingsViewModel sut = BuildViewModel(settingsService, confirmation);
            await sut.LoadAsync();

            sut.GeneralVM.ActiveTheme = "PaperCoffee";

            bool mayLeave = await sut.CanLeaveAsync();

            Assert.True(mayLeave);
            Assert.Equal(1, confirmation.CallCount);
            Assert.Equal(1, settingsService.SaveCallCount);
            Assert.False(sut.HasUnsavedChanges);
        }

        [Fact]
        public async Task Verlassen_MitAblehnungNavigiertOhneZuSpeichern()
        {
            // "Nein" verwirft die Änderung bewusst — die Seite darf trotzdem verlassen werden,
            // sonst sitzt der Nutzer in den Einstellungen fest.
            FakeAppSettingsDataService settingsService = new();
            FakeConfirmationDialogService confirmation = new(result: false);
            SettingsViewModel sut = BuildViewModel(settingsService, confirmation);
            await sut.LoadAsync();

            sut.GeneralVM.ActiveTheme = "PaperCoffee";

            bool mayLeave = await sut.CanLeaveAsync();

            Assert.True(mayLeave);
            Assert.Equal(1, confirmation.CallCount);
            Assert.Equal(0, settingsService.SaveCallCount);
            Assert.True(sut.HasUnsavedChanges);
        }

        [Fact]
        public async Task Speichern_WarntWennOnlineOhneAnbieterBleibt()
        {
            // Ohne Anbieter bleibt die Online-Mediathek unsichtbar. Wer den Offline-Modus
            // abschaltet und keinen Anbieter wählt, bekommt sonst eine leere Oberfläche.
            FakeErrorDialogService errorDialog = new();
            SettingsViewModel sut = BuildViewModel(
                new FakeAppSettingsDataService(new AppSettings
                {
                    OfflineMode = false,
                    ActiveProvider = ProviderType.None
                }),
                errorDialogService: errorDialog);

            await sut.LoadAsync();
            await sut.SaveAsync();

            _ = Assert.Single(errorDialog.ShownDialogs);
        }

        [Fact]
        public async Task Speichern_WarntNichtWennEinAnbieterGewaehltIst()
        {
            FakeErrorDialogService errorDialog = new();
            SettingsViewModel sut = BuildViewModel(
                new FakeAppSettingsDataService(new AppSettings
                {
                    OfflineMode = false,
                    ActiveProvider = ProviderType.AppleMusic
                }),
                errorDialogService: errorDialog);

            await sut.LoadAsync();
            await sut.SaveAsync();

            Assert.Empty(errorDialog.ShownDialogs);
        }

        [Fact]
        public async Task Sprachwechsel_SpeichertUndStartetNachBestaetigung()
        {
            FakeAppSettingsDataService settingsService = new();
            FakeLanguageSwitchService languageSwitch = new();
            SettingsViewModel sut = BuildViewModel(
                settingsService,
                new FakeConfirmationDialogService(result: true),
                languageSwitchService: languageSwitch);

            await sut.LoadAsync();
            sut.GeneralVM.ActiveTheme = "PaperCoffee";

            await sut.ChangeLanguageAsync("en");

            Assert.Equal(1, settingsService.SaveCallCount);
            AppSettings stored = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("en", stored.ActiveLanguage);
            // Offene Änderungen der Seite gehen beim Neustart nicht verloren
            Assert.Equal("PaperCoffee", stored.ActiveTheme);
            Assert.Equal("en", Assert.Single(languageSwitch.ChangedLanguages));
            Assert.False(sut.HasUnsavedChanges);
        }

        [Fact]
        public async Task Sprachwechsel_BleibtOhneBestaetigungAus()
        {
            FakeAppSettingsDataService settingsService = new();
            FakeLanguageSwitchService languageSwitch = new();
            SettingsViewModel sut = BuildViewModel(
                settingsService,
                new FakeConfirmationDialogService(result: false),
                languageSwitchService: languageSwitch);

            await sut.LoadAsync();

            await sut.ChangeLanguageAsync("en");

            Assert.Equal(0, settingsService.SaveCallCount);
            Assert.Empty(languageSwitch.ChangedLanguages);
        }

        [Fact]
        public async Task Sprachwechsel_MeldetDenAusgebliebenenNeustart()
        {
            // Die Sprache steht in der Datenbank, der Neustart kam nicht zustande.
            // Ohne Hinweis hielte der Nutzer die Auswahl für verloren.
            FakeErrorDialogService errorDialog = new();
            FakeLanguageSwitchService languageSwitch = new() { ChangeResult = false };
            SettingsViewModel sut = BuildViewModel(
                new FakeAppSettingsDataService(),
                errorDialogService: errorDialog,
                languageSwitchService: languageSwitch);

            await sut.LoadAsync();

            await sut.ChangeLanguageAsync("en");

            (string Title, string Message) dialog = Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("LanguageRestartTitle", dialog.Title);
            Assert.Equal("LanguageRestartManualMessage", dialog.Message);
        }

        [Fact]
        public async Task Sprachwechsel_IgnoriertLeerenCode()
        {
            FakeAppSettingsDataService settingsService = new();
            FakeConfirmationDialogService confirmation = new();
            SettingsViewModel sut = BuildViewModel(settingsService, confirmation);

            await sut.LoadAsync();
            await sut.ChangeLanguageAsync("  ");

            Assert.Equal(0, confirmation.CallCount);
            Assert.Equal(0, settingsService.SaveCallCount);
        }

        [Fact]
        public async Task Sprachwechsel_TutNichtsOhneGeladeneEinstellungen()
        {
            // Ohne vorheriges Laden gibt es keine Entität, die geschrieben werden dürfte.
            FakeAppSettingsDataService settingsService = new();
            FakeConfirmationDialogService confirmation = new();
            SettingsViewModel sut = BuildViewModel(settingsService, confirmation);

            await sut.ChangeLanguageAsync("en");

            Assert.Equal(0, confirmation.CallCount);
            Assert.Equal(0, settingsService.SaveCallCount);
        }
    }
}
