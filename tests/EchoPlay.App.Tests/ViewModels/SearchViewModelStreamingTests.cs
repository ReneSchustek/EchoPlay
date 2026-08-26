using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests der laufenden Trefferanzeige: Jede Karte erscheint, sobald ihr Treffer
    /// feststeht, und rückt dabei an ihre Stelle in der Reihenfolge.
    /// </summary>
    /// <remarks>
    /// Vorher wurde die Liste erst geschrieben, wenn der letzte Künstler bewertet war. Bei
    /// zweieinhalb Minuten Laufzeit war das kein Ladezustand mehr, sondern eine tote Seite.
    /// </remarks>
    public sealed class SearchViewModelStreamingTests
    {
        private static readonly TimeSpan Frist = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task Search_ShowsTheFirstCardWhileTheSearchIsStillRunning()
        {
            StreamingSeriesImportSearch search = new();
            SearchViewModel vm = BuildViewModel(search);

            Task firstCard = WaitForResultChange(vm);

            vm.SearchText = "Bibi";
            vm.SearchCommand.Execute(null);

            search.Publish(Series("Bibi Blocksberg", 100));
            await firstCard.WaitAsync(Frist, TestContext.Current.CancellationToken);

            // Die Suche läuft noch — der zweite Treffer ist nicht einmal angefragt.
            _ = Assert.Single(vm.Results);
            Assert.Equal("Bibi Blocksberg", vm.Results[0].Title);
            Assert.True(vm.IsLoading);

            search.Complete();
            await vm.WaitForSearchCompleteAsync().WaitAsync(Frist, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Search_InsertsALateButBetterHitAboveTheEarlierOne()
        {
            StreamingSeriesImportSearch search = new();
            SearchViewModel vm = BuildViewModel(search);

            Task firstCard = WaitForResultChange(vm);

            vm.SearchText = "Bibi";
            vm.SearchCommand.Execute(null);

            // Zuerst ein schwacher Treffer ohne Namensbezug …
            search.Publish(Series("Benjamin Blümchen", 100));
            await firstCard.WaitAsync(Frist, TestContext.Current.CancellationToken);

            Task secondCard = WaitForResultChange(vm);

            // … danach der Namenstreffer. Er gehört nach oben, obwohl er später kommt.
            search.Publish(Series("Bibi und Tina", 60));
            await secondCard.WaitAsync(Frist, TestContext.Current.CancellationToken);

            search.Complete();
            await vm.WaitForSearchCompleteAsync().WaitAsync(Frist, TestContext.Current.CancellationToken);

            Assert.Equal(2, vm.Results.Count);
            Assert.Equal("Bibi und Tina", vm.Results[0].Title);
            Assert.Equal("Benjamin Blümchen", vm.Results[1].Title);
        }

        [Fact]
        public async Task Search_PutsHitsFromTheOwnLibraryAtTheEnd()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "Bibi Blocksberg" }, cancellationToken: TestContext.Current.CancellationToken);

            StreamingSeriesImportSearch search = new();
            SearchViewModel vm = BuildViewModel(search, seriesService);

            vm.SearchText = "Bibi";
            vm.SearchCommand.Execute(null);

            search.Publish(Series("Bibi und Tina", 100));
            search.Complete();

            await vm.WaitForSearchCompleteAsync().WaitAsync(Frist, TestContext.Current.CancellationToken);

            Assert.Equal(2, vm.Results.Count);
            Assert.Equal("Bibi und Tina", vm.Results[0].Title);

            // Der eigene Bestand steht hinter den Anbieter-Treffern: Er ist schon da und
            // muss nicht importiert werden.
            Assert.Equal("Bibi Blocksberg", vm.Results[1].Title);
            Assert.True(vm.Results[1].IsImported);
        }

        [Fact]
        public async Task Search_ForALocalHit_AsksTheCoverPipelineWithTheDatabaseId()
        {
            // Der lokale Treffer hat keine Anbieter-Adresse. Damit die Kachel trotzdem ein
            // Bild bekommt, muss die Quelle „Lokal" mit der Datenbank-Kennung ankommen —
            // nur damit findet die Cover-Kette das gespeicherte Bild.
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Bibi Blocksberg" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            StreamingSeriesImportSearch search = new();
            FakeBackgroundCoverService coverService = BuildCoverService(seriesService);
            SearchViewModel vm = BuildViewModel(search, seriesService, coverService);

            vm.SearchText = "Bibi";
            vm.SearchCommand.Execute(null);

            search.Complete();
            await vm.WaitForSearchCompleteAsync().WaitAsync(Frist, TestContext.Current.CancellationToken);

            SearchResultViewModel card = Assert.Single(vm.Results);
            await (card.CoverLoadTask ?? Task.CompletedTask).WaitAsync(Frist, TestContext.Current.CancellationToken);

            (string Source, string SourceSeriesId, string? CoverUrl, System.Threading.CancellationToken Ct) call =
                Assert.Single(coverService.SearchCoverRequests);
            Assert.Equal("Lokal", call.Source);
            Assert.Equal(series.Id.ToString(), call.SourceSeriesId);
            Assert.Null(call.CoverUrl);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Liefert einen Task, der abschließt, sobald eine Karte in die Trefferliste kommt.
        /// So wartet der Test auf ein Ereignis statt auf eine Zeitspanne.
        /// </summary>
        /// <remarks>
        /// Nur das Hinzufügen zählt: Jede Suche leert die Liste zuerst, und dieses Leeren
        /// ist ebenfalls eine Änderung — darauf zu warten hieße, auf den Startschuss zu
        /// warten statt auf den ersten Treffer.
        /// </remarks>
        private static Task WaitForResultChange(SearchViewModel vm)
        {
            TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.Action != NotifyCollectionChangedAction.Add)
                {
                    return;
                }

                vm.Results.CollectionChanged -= OnChanged;
                _ = changed.TrySetResult();
            }

            vm.Results.CollectionChanged += OnChanged;
            return changed.Task;
        }

        private static ImportSeries Series(string title, int score) => new()
        {
            Title = title,
            Source = "AppleMusic",
            SourceSeriesId = title,
            IsHoerspiel = true,
            Score = score
        };

        /// <summary>Baut die Cover-Pipeline als Nachbau, der jede Anfrage aufzeichnet.</summary>
        private static FakeBackgroundCoverService BuildCoverService(FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());

            return new FakeBackgroundCoverService(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverDownloader());
        }

        private static SearchViewModel BuildViewModel(
            ISeriesImportSearch search,
            FakeSeriesDataService? seriesService = null,
            FakeBackgroundCoverService? coverService = null)
        {
            FakeSeriesDataService series = seriesService ?? new FakeSeriesDataService();

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.AppleMusic }));
            _ = services.AddKeyedScoped<ISeriesImportSearch>("AppleMusic", (_, _) => search);
            _ = services.AddKeyedScoped<ISeriesImportSearch>("Spotify", (_, _) => search);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(FakeSpotifyClientCredentialsProvider.WithCredentials());
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

            return new SearchViewModel(
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                scopeFactory,
                backgroundCoverService: coverService);
        }
    }
}
