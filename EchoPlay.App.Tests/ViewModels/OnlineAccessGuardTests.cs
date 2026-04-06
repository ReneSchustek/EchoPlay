using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Online-Access-Guard-Logik und den temporären Online-Status in der StatusBar.
    /// Prüft, dass der Guard im Offline-Modus den Bestätigungsdialog aufruft
    /// und die StatusBar temporär auf "Online" schaltet.
    /// </summary>
    public sealed class OnlineAccessGuardTests
    {
        // ── IsTemporarilyOnline in StatusBarViewModel ────────────────────────────

        [Fact]
        public void IsTemporarilyOnline_DefaultIsFalse()
        {
            // Nach der Initialisierung ist kein temporärer Online-Status aktiv
            StatusBarViewModel vm = BuildStatusBarViewModel();

            Assert.False(vm.IsTemporarilyOnline);
        }

        [Fact]
        public void OnlineOfflineText_ShowsOnline_WhenTemporarilyOnlineInOfflineMode()
        {
            // Offline-Modus + temporär online → Anzeige muss "Online" zeigen
            StatusBarViewModel vm = BuildStatusBarViewModel(offlineMode: true);

            vm.IsTemporarilyOnline = true;

            Assert.Equal("Online", vm.OnlineOfflineText);
        }

        [Fact]
        public void OnlineOfflineText_ShowsOffline_WhenNotTemporarilyOnline()
        {
            // Offline-Modus ohne temporären Status → "Offline"
            StatusBarViewModel vm = BuildStatusBarViewModel(offlineMode: true);

            Assert.Equal("Offline", vm.OnlineOfflineText);
        }

        [Fact]
        public void OnlineOfflineGlyph_ShowsWifi_WhenTemporarilyOnline()
        {
            // Temporär online → Wifi-Symbol statt Flugzeug
            StatusBarViewModel vm = BuildStatusBarViewModel(offlineMode: true);

            vm.IsTemporarilyOnline = true;

            // E701 = Wifi-Icon
            Assert.Equal("\uE701", vm.OnlineOfflineGlyph);
        }

        [Fact]
        public void OnlineOfflineGlyph_ShowsPlane_WhenOfflineAndNotTemporarilyOnline()
        {
            // Offline-Modus ohne temporären Status → Flugzeug-Symbol
            StatusBarViewModel vm = BuildStatusBarViewModel(offlineMode: true);

            // E709 = Flugzeug-Icon
            Assert.Equal("\uE709", vm.OnlineOfflineGlyph);
        }

        [Fact]
        public void IsTemporarilyOnline_RaisesPropertyChanged_ForAllVisualProperties()
        {
            // Alle visuellen Properties müssen aktualisiert werden, damit die UI reagiert
            StatusBarViewModel vm = BuildStatusBarViewModel(offlineMode: true);
            List<string> changedProperties = [];
            vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

            vm.IsTemporarilyOnline = true;

            Assert.Contains(nameof(StatusBarViewModel.OnlineOfflineText), changedProperties);
            Assert.Contains(nameof(StatusBarViewModel.OnlineOfflineGlyph), changedProperties);
            Assert.Contains(nameof(StatusBarViewModel.OnlineOfflineBrush), changedProperties);
            Assert.Contains(nameof(StatusBarViewModel.OfflineSymbolVisibility), changedProperties);
        }

        // ── FakeOnlineAccessGuard – Verhalten in ViewModels ─────────────────────

        [Fact]
        public async Task ImportSearch_Aborted_WhenGuardReturnsNull()
        {
            // Nutzer lehnt ab → Suche wird nicht ausgeführt, Results bleiben leer
            FakeOnlineAccessGuard guard = new(allowAccess: false);
            ImportViewModel vm = BuildImportViewModel(guard);

            vm.SearchQuery = "TKKG";
            await vm.SearchAsync();

            Assert.Empty(vm.Results);
            Assert.Equal(1, guard.CallCount);
        }

        [Fact]
        public async Task ImportSearch_Executes_WhenGuardAllows()
        {
            // Nutzer bestätigt oder Online-Modus → Suche wird normal ausgeführt
            FakeOnlineAccessGuard guard = new(allowAccess: true);
            ImportViewModel vm = BuildImportViewModel(guard);

            vm.SearchQuery = "TKKG";
            await vm.SearchAsync();

            // Suche wurde ausgeführt (auch wenn 0 Ergebnisse, weil Provider=None)
            Assert.Equal(1, guard.CallCount);
        }

        // ── Hilfsmethoden ────────────────────────────────────────────────────────

        /// <summary>
        /// Baut eine <see cref="StatusBarViewModel"/>-Instanz mit optionalem Offline-Modus.
        /// </summary>
        private static StatusBarViewModel BuildStatusBarViewModel(bool offlineMode = false)
        {
            FakeAppSettingsDataService settingsService = new(new AppSettings { OfflineMode = offlineMode });

            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            services.AddScoped<IAppSettingsDataService>(_ => settingsService);

            ServiceProvider provider = services.BuildServiceProvider();

            StatusBarViewModel vm = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService());

            // Offline-Status initial laden
            vm.LoadAsync().GetAwaiter().GetResult();

            return vm;
        }

        /// <summary>
        /// Baut eine <see cref="ImportViewModel"/>-Instanz mit konfigurierbarem Guard.
        /// </summary>
        private static ImportViewModel BuildImportViewModel(FakeOnlineAccessGuard guard)
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.None });

            ServiceCollection services = new();
            services.AddScoped<IAppSettingsDataService>(_ => settings);
            services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([]));
            services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            services.AddSingleton<EchoPlay.App.Services.CoverService>();
            services.AddSingleton<EchoPlay.App.Services.EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            EchoPlay.App.Services.ImportService importService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EchoPlay.App.Services.EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            return new ImportViewModel(importService, new FakeErrorDialogService(), guard, new FakeLocalizationService());
        }
    }
}
