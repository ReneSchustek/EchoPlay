using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Net.Http;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="SearchResultViewModel"/>: prüfen den Cover-Lade-Pfad
    /// über die zentrale Cover-Pipeline (<see cref="BackgroundCoverService"/>).
    /// Der Brightness-Analyzer und die <see cref="Microsoft.UI.Xaml.Media.Imaging.BitmapImage"/>-
    /// Erzeugung können in Unit-Tests nicht laufen – die Verifikation beschränkt sich
    /// auf die Argumente, mit denen die Pipeline aufgerufen wird.
    /// </summary>
    public sealed class SearchResultViewModelTests
    {
        [Fact]
        public async Task CoverLoad_UsesDbFirst_WhenCached()
        {
            // Trefferkachel ruft die zentrale Cover-Pipeline mit Source/SourceSeriesId/Url
            // auf. Der DB-First-Lookup selbst ist Sache des Service – der VM-Test verifiziert
            // den Vertrag (richtige Argumente, Ergebnis wird konsumiert).
            FakeBackgroundCoverService coverService = BuildFakeBackgroundCoverService();
            coverService.SearchCoverResponse = [0xAA, 0xBB];

            ImportSeries series = new()
            {
                Title = "TKKG",
                Source = "Spotify",
                SourceSeriesId = "tkkg-001",
                CoverImageUrl = "https://example.com/cover.jpg"
            };

            SearchResultViewModel sut = new(
                series,
                isAlreadyImported: false,
                importService: null!,
                errorDialogService: new FakeErrorDialogService(),
                localizationService: new FakeLocalizationService(),
                backgroundCoverService: coverService);

            await (sut.CoverLoadTask ?? Task.CompletedTask);

            (string Source, string SourceSeriesId, string? CoverUrl, CancellationToken Ct) call =
                Assert.Single(coverService.SearchCoverRequests);
            Assert.Equal("Spotify", call.Source);
            Assert.Equal("tkkg-001", call.SourceSeriesId);
            Assert.Equal("https://example.com/cover.jpg", call.CoverUrl);
        }

        [Fact]
        public async Task ClearCoverImage_NullsBitmap()
        {
            // Memory-Hygiene: Trefferkachel muss ihre BitmapImage-Referenz freigeben, sobald
            // SearchViewModel.Reset oder eine neue Suche die Liste austauscht. Sonst hängt
            // jede Karte die Cover-Bytes bis zum nächsten GC-Lauf am Heap.
            FakeBackgroundCoverService coverService = BuildFakeBackgroundCoverService();
            coverService.SearchCoverResponse = null;

            ImportSeries series = new()
            {
                Title = "TKKG",
                Source = "Spotify",
                SourceSeriesId = "tkkg-001",
                CoverImageUrl = "https://example.com/cover.jpg"
            };

            SearchResultViewModel sut = new(
                series,
                isAlreadyImported: false,
                importService: null!,
                errorDialogService: new FakeErrorDialogService(),
                localizationService: new FakeLocalizationService(),
                backgroundCoverService: coverService);

            await (sut.CoverLoadTask ?? Task.CompletedTask);

            // Property-Changed-Trail einsammeln: ClearCoverImage() darf den Bindings-Reset
            // erzwingen, auch wenn das Bild bereits null war (idempotent + UI-konsistent).
            List<string?> changes = [];
            sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            sut.ClearCoverImage();
            sut.ClearCoverImage();

            Assert.Null(sut.CoverImage);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, sut.NoCoverVisibility);
        }

        [Fact]
        public async Task CoverLoad_CancelsWhenTokenRequested()
        {
            // Wird das Eltern-VM-Token vor dem Start abgebrochen, muss die Trefferkachel
            // den Cancel transparent durchreichen – damit alte Suchläufe bei einer neuen
            // Sucheingabe wirklich keine HTTP-Requests mehr starten.
            FakeBackgroundCoverService coverService = BuildFakeBackgroundCoverService();

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            ImportSeries series = new()
            {
                Title = "TKKG",
                Source = "Spotify",
                SourceSeriesId = "tkkg-001",
                CoverImageUrl = "https://example.com/cover.jpg"
            };

            SearchResultViewModel sut = new(
                series,
                isAlreadyImported: false,
                importService: null!,
                errorDialogService: new FakeErrorDialogService(),
                localizationService: new FakeLocalizationService(),
                backgroundCoverService: coverService,
                cancellationToken: cts.Token);

            await (sut.CoverLoadTask ?? Task.CompletedTask);

            (string Source, string SourceSeriesId, string? CoverUrl, CancellationToken Ct) call =
                Assert.Single(coverService.SearchCoverRequests);
            Assert.True(call.Ct.IsCancellationRequested);
        }

        [Fact]
        public void Trefferkachel_ZeigtDieHerkunftLesbar()
        {
            // "AppleMusic" ist der interne Schlüssel; auf der Kachel steht der Name des Anbieters.
            SearchResultViewModel apple = BuildViewModel(new ImportSeries
            {
                Title = "TKKG",
                Source = ProviderKeys.AppleMusic,
                SourceSeriesId = "am-1"
            });

            SearchResultViewModel spotify = BuildViewModel(new ImportSeries
            {
                Title = "TKKG",
                Source = "Spotify",
                SourceSeriesId = "sp-1"
            });

            Assert.Equal("Apple Music", apple.SourceLabel);
            Assert.Equal("Spotify", spotify.SourceLabel);
        }

        [Fact]
        public void Albumtreffer_TraegtDenKuenstlerImTitel()
        {
            SearchResultViewModel sut = BuildViewModel(new ImportSeries
            {
                Title = "Der Superhund",
                ArtistName = "TKKG",
                IsAlbumResult = true,
                Source = "Spotify",
                SourceSeriesId = "sp-1"
            });

            Assert.Equal("TKKG – Der Superhund", sut.Title);
            Assert.True(sut.IsAlbumResult);
        }

        [Fact]
        public void OhneCover_ZeigtDieKachelDenPlatzhalterUndDenHellenRahmen()
        {
            SearchResultViewModel sut = BuildViewModel();

            Assert.Equal(Visibility.Visible, sut.NoCoverVisibility);
            Assert.Equal(Visibility.Visible, sut.LightBorderVisibility);
            Assert.Equal(Visibility.Collapsed, sut.DarkBorderVisibility);
        }

        [Fact]
        public void BereitsVorhandeneSerie_ZeigtKeinenHinzufuegenKnopf()
        {
            SearchResultViewModel sut = BuildViewModel(isAlreadyImported: true);

            Assert.Equal(Visibility.Collapsed, sut.ImportButtonVisibility);
            Assert.Equal(Visibility.Visible, sut.AlreadyImportedVisibility);
            // Ohne Auswahlmöglichkeit gibt es auch keinen Rahmen für die Mehrfachauswahl
            Assert.Equal(Visibility.Collapsed, sut.LightBorderVisibility);
            Assert.Equal(Visibility.Collapsed, sut.DarkBorderVisibility);
        }

        [Fact]
        public void Mehrfachauswahl_MerktSichDieKachel()
        {
            SearchResultViewModel sut = BuildViewModel();

            sut.IsSelected = true;

            Assert.True(sut.IsSelected);
        }

        [Fact]
        public async Task Add_ReportsTheSeriesAsPresentAndCallsTheCallerBack()
        {
            bool nachbereitet = false;
            SearchResultViewModel sut = BuildViewModel(
                onImportCompleted: () =>
                {
                    nachbereitet = true;
                    return Task.CompletedTask;
                });

            sut.ImportCommand.Execute(null);
            await ChangeSignals.WaitForAsync(sut, () => sut.IsImported, "Serie gilt als hinzugefügt");

            Assert.True(nachbereitet);
            Assert.Equal(Visibility.Collapsed, sut.ImportButtonVisibility);
            Assert.Equal(Visibility.Visible, sut.AlreadyImportedVisibility);
            Assert.False(sut.IsImporting);
        }

        [Fact]
        public async Task Hinzufügen_ZeigtDenFehlerUndLässtDenKnopfStehen()
        {
            // Scheitert der Abruf beim Anbieter, darf die Kachel nicht als erledigt
            // erscheinen — sonst hält der Nutzer die Serie für vorhanden.
            FakeErrorDialogService errorDialog = new();
            SearchResultViewModel sut = BuildViewModel(
                errorDialogService: errorDialog,
                failForSourceSeriesId: "sp-1");

            sut.ImportCommand.Execute(null);
            (string Title, string Message) dialog =
                await errorDialog.FirstDialogShown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("OnlineImportFailedTitle", dialog.Title);
            Assert.False(sut.IsImported);
            Assert.False(sut.IsImporting);
            Assert.Equal(Visibility.Visible, sut.ImportButtonVisibility);
        }

        /// <summary>
        /// Baut die Trefferkachel mit einem echten <see cref="ImportService"/> auf Fakes:
        /// Der Dienst ist versiegelt, und der Weg vom Klick bis zur gespeicherten Serie
        /// ist genau das, was hier geprüft werden soll.
        /// </summary>
        private static SearchResultViewModel BuildViewModel(
            ImportSeries? series = null,
            bool isAlreadyImported = false,
            FakeErrorDialogService? errorDialogService = null,
            Func<Task>? onImportCompleted = null,
            string? failForSourceSeriesId = null)
        {
            ImportSeries treffer = series ?? new ImportSeries
            {
                Title = "TKKG",
                Source = "Spotify",
                SourceSeriesId = "sp-1"
            };

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.Spotify }));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([], failForSourceSeriesId));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([], failForSourceSeriesId));
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            ImportService importService = new(
                scopeFactory,
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            return new SearchResultViewModel(
                treffer,
                isAlreadyImported,
                importService,
                errorDialogService ?? new FakeErrorDialogService(),
                new FakeLocalizationService(),
                onImportCompleted: onImportCompleted);
        }

        private static FakeBackgroundCoverService BuildFakeBackgroundCoverService()
        {
            // Minimale DI-Infrastruktur: BackgroundCoverService verlangt eine Scope-Factory
            // und einen Cover-Downloader, beide werden vom Such-Treffer-Pfad nicht erreicht
            // (der Fake überschreibt RequestCoverForSearchResultAsync vollständig).
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new FakeBackgroundCoverService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverDownloader());
        }
    }
}
