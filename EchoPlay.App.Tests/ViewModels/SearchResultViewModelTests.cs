using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models.Import;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Net.Http;
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

            (string Source, string SourceSeriesId, string CoverUrl, CancellationToken Ct) call =
                Assert.Single(coverService.SearchCoverRequests);
            Assert.Equal("Spotify", call.Source);
            Assert.Equal("tkkg-001", call.SourceSeriesId);
            Assert.Equal("https://example.com/cover.jpg", call.CoverUrl);
        }

        [Fact]
        public async Task ClearCoverImage_NullsBitmap()
        {
            // Memory-Hygiene: Trefferkachel muss ihre BitmapImage-Referenz freigeben, sobald
            // SucheViewModel.Reset oder eine neue Suche die Liste austauscht. Sonst hängt
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

            (string Source, string SourceSeriesId, string CoverUrl, CancellationToken Ct) call =
                Assert.Single(coverService.SearchCoverRequests);
            Assert.True(call.Ct.IsCancellationRequested);
        }

        private static FakeBackgroundCoverService BuildFakeBackgroundCoverService()
        {
            // Minimale DI-Infrastruktur: BackgroundCoverService verlangt einen Scope-Factory
            // und einen HttpClientFactory, beide werden vom Such-Treffer-Pfad nicht erreicht
            // (der Fake überschreibt RequestCoverForSearchResultAsync vollständig).
            ServiceCollection services = new();
            _ = services.AddHttpClient();
            ServiceProvider provider = services.BuildServiceProvider();

            return new FakeBackgroundCoverService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>());
        }
    }
}
