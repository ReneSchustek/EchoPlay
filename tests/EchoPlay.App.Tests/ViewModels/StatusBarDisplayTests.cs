using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Anzeigeseite der Info-Leiste: Badge-Texte, Sichtbarkeiten,
    /// Anbieter-Anzeige sowie Theme- und Sprachwechsel.
    /// Die Statistik-Berechnung selbst steht in <see cref="StatusBarViewModelTests"/>.
    /// </summary>
    public sealed class StatusBarDisplayTests
    {
        private static StatusBarViewModel BuildViewModel(
            AppSettings? settings = null,
            FakeThemeService? themeService = null,
            FakeAppSettingsDataService? settingsService = null,
            ISpotifyClientCredentialsProvider? credentialsProvider = null,
            FakeLanguageSwitchService? languageSwitchService = null,
            FakeSeriesDataService? seriesService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => settingsService ?? new FakeAppSettingsDataService(settings));
            _ = services.AddScoped<ILocalizationService>(_ => new FakeLocalizationService(
                new Dictionary<string, string> { ["StatusBarSpotifyDisconnected"] = "Spotify getrennt" }));

            if (credentialsProvider is not null)
            {
                _ = services.AddScoped(_ => credentialsProvider);
            }

            ServiceProvider provider = services.BuildServiceProvider();

            return new StatusBarViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                themeService ?? new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock(),
                languageSwitchService);
        }

        [Fact]
        public async Task Badges_ZeigenEinzahlUndMehrzahl()
        {
            // Die Info-Leiste beschriftet die Zahl selbst — bei genau einer Serie
            // darf dort nicht "1 Serien" stehen.
            FakeSeriesDataService oneSeries = new();
            await oneSeries.AddAsync(
                new Series { Title = "TKKG", IsSubscribed = true },
                cancellationToken: TestContext.Current.CancellationToken);

            StatusBarViewModel singular = BuildViewModel(seriesService: oneSeries);
            await singular.LoadAsync();

            FakeSeriesDataService twoSeries = new();
            await twoSeries.AddAsync(
                new Series { Title = "TKKG", IsSubscribed = true },
                cancellationToken: TestContext.Current.CancellationToken);
            await twoSeries.AddAsync(
                new Series { Title = "Bibi", IsSubscribed = true },
                cancellationToken: TestContext.Current.CancellationToken);

            StatusBarViewModel plural = BuildViewModel(seriesService: twoSeries);
            await plural.LoadAsync();

            Assert.Contains("1 Serie", singular.SubscribedSeriesText, StringComparison.Ordinal);
            Assert.DoesNotContain("Serien", singular.SubscribedSeriesText, StringComparison.Ordinal);
            Assert.Contains("2 Serien", plural.SubscribedSeriesText, StringComparison.Ordinal);
        }

        [Fact]
        public void Badges_ZeigenDieZahlInJedemText()
        {
            // Gehört, offen und neu teilen sich denselben Aufbau: Muster plus Zahl.
            StatusBarViewModel sut = BuildViewModel();

            Assert.Contains("0", sut.FinishedEpisodesText, StringComparison.Ordinal);
            Assert.Contains("0", sut.UnfinishedEpisodesText, StringComparison.Ordinal);
            Assert.Contains("0", sut.NewEpisodesText, StringComparison.Ordinal);
        }

        [Fact]
        public void NeueFolgen_OhneTrefferBleibenBadgeUndTrennerAus()
        {
            // Ohne neue Folgen zeigt die Leiste weder Badge noch Trennstrich —
            // sonst steht dort ein Hinweis auf nichts.
            StatusBarViewModel sut = BuildViewModel();

            Assert.Equal(Visibility.Collapsed, sut.NewEpisodesVisibility);
            Assert.Equal(Visibility.Collapsed, sut.NewEpisodesSeparatorVisibility);
        }

        [Fact]
        public async Task Refresh_UebernimmtThemeUndSpracheAusDenEinstellungen()
        {
            AppSettings settings = new()
            {
                ActiveTheme = "PaperCoffee",
                ActiveLanguage = "en",
                ActiveProvider = ProviderType.AppleMusic
            };

            StatusBarViewModel sut = BuildViewModel(settings);
            await sut.RefreshAsync();

            Assert.Equal("PaperCoffee", sut.ActiveTheme);
            Assert.Equal("en", sut.ActiveLanguage);
            Assert.Equal("EN", sut.ActiveLanguageDisplay);
        }

        [Fact]
        public async Task Anbieteranzeige_NenntAppleMusicMitLeerzeichen()
        {
            StatusBarViewModel sut = BuildViewModel(new AppSettings { ActiveProvider = ProviderType.AppleMusic });
            await sut.RefreshAsync();

            Assert.Equal("Apple Music", sut.ActiveProviderDisplay);
        }

        [Fact]
        public async Task Anbieteranzeige_NenntBeideAnbieter()
        {
            StatusBarViewModel sut = BuildViewModel(new AppSettings { ActiveProvider = ProviderType.Both });
            await sut.RefreshAsync();

            Assert.Equal("Spotify + Apple Music", sut.ActiveProviderDisplay);
        }

        [Fact]
        public async Task Anbieteranzeige_BleibtLeerOhneGewaehltenAnbieter()
        {
            // Ohne Anbieter bleibt das Feld leer und der Menüeintrag "Online-Mediathek" weg.
            StatusBarViewModel sut = BuildViewModel(new AppSettings { ActiveProvider = ProviderType.None });
            await sut.RefreshAsync();

            Assert.Equal(string.Empty, sut.ActiveProviderDisplay);
            Assert.Equal(Visibility.Collapsed, sut.OnlineMediathekVisibility);
        }

        [Fact]
        public async Task Anbieteranzeige_MeldetSpotifyOhneZugangsdatenAlsGetrennt()
        {
            // Ohne hinterlegte Zugangsdaten sucht die App über Apple Music weiter.
            // Die Leiste darf Spotify dann nicht als verbunden ausweisen.
            StatusBarViewModel sut = BuildViewModel(
                new AppSettings { ActiveProvider = ProviderType.Spotify },
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            await sut.RefreshAsync();

            Assert.Equal("Spotify getrennt", sut.ActiveProviderDisplay);
        }

        [Fact]
        public async Task Anbieteranzeige_NenntSpotifyMitZugangsdaten()
        {
            StatusBarViewModel sut = BuildViewModel(
                new AppSettings { ActiveProvider = ProviderType.Spotify },
                credentialsProvider: FakeSpotifyClientCredentialsProvider.WithCredentials());

            await sut.RefreshAsync();

            Assert.Equal("Spotify", sut.ActiveProviderDisplay);
        }

        [Fact]
        public async Task Offlinemodus_ZeigtSymbolUndVerbirgtDieOnlineMediathek()
        {
            StatusBarViewModel sut = BuildViewModel(new AppSettings
            {
                ActiveProvider = ProviderType.AppleMusic,
                OfflineMode = true
            });

            await sut.RefreshAsync();

            Assert.True(sut.IsOffline);
            Assert.Equal(Visibility.Visible, sut.OfflineSymbolVisibility);
            Assert.Equal(Visibility.Collapsed, sut.OnlineMediathekVisibility);
            Assert.Equal("Offline", sut.OnlineOfflineText);
            Assert.Equal("\uE709", sut.OnlineOfflineGlyph);
        }

        [Fact]
        public async Task VoruebergehendOnline_BlendetDasOfflineSymbolAus()
        {
            // Eine einzelne Aktion darf im Offline-Modus online gehen; die Anzeige
            // folgt ihr, ohne die Einstellung selbst zu verändern.
            StatusBarViewModel sut = BuildViewModel(new AppSettings { OfflineMode = true });
            await sut.RefreshAsync();

            sut.IsTemporarilyOnline = true;

            Assert.True(sut.IsOffline);
            Assert.Equal(Visibility.Collapsed, sut.OfflineSymbolVisibility);
            Assert.Equal("Online", sut.OnlineOfflineText);
            Assert.Equal("\uE701", sut.OnlineOfflineGlyph);
        }

        [Fact]
        public void UngespeicherteEinstellungen_SteuernDenHinweis()
        {
            StatusBarViewModel sut = BuildViewModel();

            Assert.Equal(Visibility.Collapsed, sut.UnsavedSettingsVisibility);

            sut.HasUnsavedSettings = true;

            Assert.Equal(Visibility.Visible, sut.UnsavedSettingsVisibility);
        }

        [Fact]
        public void ThemeWechsel_WendetDasThemaAnUndMerktEsSich()
        {
            FakeThemeService themeService = new();
            StatusBarViewModel sut = BuildViewModel(themeService: themeService);

            sut.SwitchThemeCommand.Execute("PaperCoffee");

            Assert.Equal("PaperCoffee", Assert.Single(themeService.AppliedThemes));
            Assert.Equal("PaperCoffee", sut.ActiveTheme);
        }

        [Fact]
        public void ThemeWechsel_IgnoriertLeerenNamen()
        {
            // Ein leerer Name käme aus einem falsch verdrahteten Menüeintrag —
            // das Thema darf davon nicht wechseln.
            FakeThemeService themeService = new();
            StatusBarViewModel sut = BuildViewModel(themeService: themeService);

            sut.SwitchTheme("   ");

            Assert.Empty(themeService.AppliedThemes);
            Assert.Equal("MidnightLibrary", sut.ActiveTheme);
        }

        [Fact]
        public async Task Sprachwechsel_LaeuftUeberDenDienstWennErRegistriertIst()
        {
            FakeLanguageSwitchService languageSwitch = new();
            FakeAppSettingsDataService settingsService = new();
            StatusBarViewModel sut = BuildViewModel(
                settingsService: settingsService,
                languageSwitchService: languageSwitch);

            await sut.ChangeLanguageAsync("en");

            Assert.Equal("en", Assert.Single(languageSwitch.ChangedLanguages));
            Assert.Equal(0, settingsService.SaveCallCount);
        }

        [Fact]
        public async Task Sprachwechsel_SpeichertOhneDienstNurDieEinstellung()
        {
            FakeAppSettingsDataService settingsService = new();
            StatusBarViewModel sut = BuildViewModel(settingsService: settingsService);

            await sut.ChangeLanguageAsync("en");

            Assert.Equal(1, settingsService.SaveCallCount);
            AppSettings stored = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("en", stored.ActiveLanguage);
        }

        [Fact]
        public async Task Sprachwechsel_IgnoriertLeerenCode()
        {
            FakeLanguageSwitchService languageSwitch = new();
            FakeAppSettingsDataService settingsService = new();
            StatusBarViewModel sut = BuildViewModel(
                settingsService: settingsService,
                languageSwitchService: languageSwitch);

            await sut.ChangeLanguageAsync(" ");

            Assert.Empty(languageSwitch.ChangedLanguages);
            Assert.Equal(0, settingsService.SaveCallCount);
        }

        [Fact]
        public void SprachwechselBefehl_ReichtDenCodeWeiter()
        {
            FakeLanguageSwitchService languageSwitch = new();
            StatusBarViewModel sut = BuildViewModel(languageSwitchService: languageSwitch);

            sut.SwitchLanguageCommand.Execute("en");

            Assert.Equal("en", Assert.Single(languageSwitch.ChangedLanguages));
        }
    }
}
