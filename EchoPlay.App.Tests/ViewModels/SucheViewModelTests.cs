using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="SucheViewModel"/>.
    /// Prüft Suche, Import-Status-Markierung und Fehlerbehandlung.
    /// </summary>
    public sealed class SucheViewModelTests
    {
        // Fester Spotify-ArtistId für reproduzierbare "bereits importiert"-Tests
        private const string KnownSpotifyId = "spotify-tkkg-001";

        /// <summary>
        /// Baut einen <see cref="ImportService"/> mit der übergebenen Suchliste.
        /// Alle Provider-Calls landen beim Fake statt bei der echten Spotify-API.
        /// </summary>
        private static ImportService BuildImportService(
            IReadOnlyList<ImportSeries> searchResults,
            FakeSeriesDataService seriesService,
            ISeriesImportSearch? overrideSearch = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.Spotify }));
            _ = services.AddKeyedScoped<ISeriesImportSearch>(
                "Spotify",
                (_, _) => overrideSearch ?? new FakeSeriesImportSearch(searchResults));
            _ = services.AddKeyedScoped<IEpisodeImportSource>(
                "Spotify",
                (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<EchoPlay.App.Services.CoverService>();
            _ = services.AddSingleton<EchoPlay.App.Services.EpisodeCoverCacheService>();
            ServiceProvider provider = services.BuildServiceProvider();

            return new ImportService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EchoPlay.App.Services.EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());
        }

        private static SucheViewModel BuildViewModel(
            IReadOnlyList<ImportSeries> searchResults,
            FakeSeriesDataService? seriesService = null,
            ISeriesImportSearch? overrideSearch = null,
            IServiceScopeFactory? scopeFactory = null)
        {
            FakeSeriesDataService series = seriesService ?? new FakeSeriesDataService();
            ImportService importService = BuildImportService(searchResults, series, overrideSearch);
            return new SucheViewModel(importService, new FakeErrorDialogService(), new FakeLocalizationService(), scopeFactory);
        }

        /// <summary>
        /// Baut eine <see cref="IServiceScopeFactory"/>, die <see cref="ISeriesDataService"/>
        /// mit der übergebenen <see cref="FakeSeriesDataService"/>-Instanz bereitstellt.
        /// Dient als Scope-Factory für die lokale Suche in <see cref="SucheViewModel"/>.
        /// </summary>
        private static IServiceScopeFactory BuildLocalScopeFactory(FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        [Fact]
        public async Task SearchCommand_ReturnsResults()
        {
            // Suchanfrage muss Ergebnisse als SearchResultViewModels bereitstellen
            List<ImportSeries> results =
            [
                new ImportSeries { Title = "TKKG",                  Source = "Spotify", SourceSeriesId = "id1" },
                new ImportSeries { Title = "Die drei Fragezeichen", Source = "Spotify", SourceSeriesId = "id2" },
            ];

            SucheViewModel vm = BuildViewModel(results);
            vm.SearchText = "Hörspiel";
            vm.SearchCommand.Execute(null);

            Assert.Equal(2, vm.Results.Count);
        }

        [Fact]
        public async Task SearchCommand_MarksAlreadyImportedSeries()
        {
            // Serien, die bereits in der DB sind, sollen als importiert markiert werden
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title           = "TKKG",
                SpotifyArtistId = KnownSpotifyId
            });

            List<ImportSeries> results =
            [
                new ImportSeries
                {
                    Title          = "TKKG",
                    Source         = "Spotify",
                    SourceSeriesId = KnownSpotifyId
                }
            ];

            SucheViewModel vm = BuildViewModel(results, seriesService);
            vm.SearchText = "TKKG";
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            _ = Assert.Single(vm.Results);
            Assert.True(vm.Results[0].IsImported);
        }

        [Fact]
        public async Task SubscribeCommand_ImportedSeriesGetsSubscribed()
        {
            // Nach erfolgreichem Import muss IsImported auf true wechseln
            List<ImportSeries> results =
            [
                new ImportSeries
                {
                    Title          = "Bibi Blocksberg",
                    Source         = "Spotify",
                    SourceSeriesId = "bibi-001"
                }
            ];

            SucheViewModel vm = BuildViewModel(results);
            vm.SearchText = "Bibi";
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            _ = Assert.Single(vm.Results);
            Assert.False(vm.Results[0].IsImported);

            // Import auslösen
            vm.Results[0].ImportCommand.Execute(null);

            Assert.True(vm.Results[0].IsImported);
        }

        [Fact]
        public async Task SubscribeCommand_ExistingSeriesGetsSubscribed()
        {
            // Eine Serie, die bereits importiert ist, zeigt von Anfang an IsImported=true
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title           = "Fünf Freunde",
                SpotifyArtistId = "ff-002"
            });

            List<ImportSeries> results =
            [
                new ImportSeries
                {
                    Title          = "Fünf Freunde",
                    Source         = "Spotify",
                    SourceSeriesId = "ff-002"
                }
            ];

            SucheViewModel vm = BuildViewModel(results, seriesService);
            vm.SearchText = "Fünf";
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            // Kein Import nötig – bereits vorhanden
            _ = Assert.Single(vm.Results);
            Assert.True(vm.Results[0].IsImported);
        }

        [Fact]
        public void SearchCommand_NetworkError_SetsErrorMessage()
        {
            // Bei Netzwerkfehler soll der ErrorDialogService informiert werden
            FakeErrorDialogService errorDialog = new();

            // FakeSeriesImportSearch wird durch eine Exception-werfende Variante ersetzt
            ThrowingSeriesImportSearch throwingSearch = new();
            FakeSeriesDataService seriesService = new();
            ImportService importService = BuildImportService([], seriesService, throwingSearch);

            SucheViewModel vm = new(importService, errorDialog, new FakeLocalizationService());
            vm.SearchText = "Irgendwas";
            vm.SearchCommand.Execute(null);

            _ = Assert.Single(errorDialog.ShownDialogs);
        }

        [Fact]
        public async Task SearchCommand_ReturnsSpotifySourceLabel()
        {
            // Spotify-Ergebnis muss "Spotify" als SourceLabel tragen
            List<ImportSeries> results =
            [
                new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "tkkg-1" }
            ];

            SucheViewModel vm = BuildViewModel(results);
            vm.SearchText = "TKKG";
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            _ = Assert.Single(vm.Results);
            Assert.Equal("Spotify", vm.Results[0].SourceLabel);
        }

        [Fact]
        public async Task SearchCommand_LocalScope_ReturnsLocalSeries()
        {
            // Bei Scope "Lokal" dürfen nur Serien aus der Datenbank kommen – kein Provider-Aufruf
            FakeSeriesDataService localData = new();
            await localData.AddAsync(new Series { Title = "TKKG" });

            IServiceScopeFactory localFactory = BuildLocalScopeFactory(localData);
            SucheViewModel vm = BuildViewModel([], scopeFactory: localFactory);
            vm.SelectedScopeIndex = 2; // Lokal
            vm.SearchText = "TKKG";
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            _ = Assert.Single(vm.Results);
            Assert.Equal("TKKG", vm.Results[0].Title);
            Assert.Equal("Lokal", vm.Results[0].SourceLabel);
            // Lokale Einträge gelten als bereits importiert – Import-Button bleibt ausgeblendet
            Assert.True(vm.Results[0].IsImported);
        }

        [Fact]
        public async Task SearchCommand_OnlineScope_IgnoresLocalSeries()
        {
            // Bei Scope "Online" dürfen keine lokalen Serien in den Ergebnissen erscheinen
            FakeSeriesDataService localData = new();
            await localData.AddAsync(new Series { Title = "Fünf Freunde" });

            IServiceScopeFactory localFactory = BuildLocalScopeFactory(localData);

            // Online-Provider liefert eine Serie
            List<ImportSeries> onlineResults =
            [
                new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "tkkg-1" }
            ];

            SucheViewModel vm = BuildViewModel(onlineResults, scopeFactory: localFactory);
            vm.SelectedScopeIndex = 1; // Online
            vm.SearchText = "e";       // würde "Fünf Freunde" lokal treffen – darf aber nicht erscheinen
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            // Nur das Online-Ergebnis – lokale "Fünf Freunde" bleibt ausgeschlossen
            _ = Assert.Single(vm.Results);
            Assert.Equal("TKKG", vm.Results[0].Title);
        }

        [Fact]
        public async Task SearchCommand_BothScope_CombinesOnlineAndLocalResults()
        {
            // Bei Scope "Alle Quellen" müssen Online- und lokale Ergebnisse gemeinsam erscheinen
            FakeSeriesDataService localData = new();
            await localData.AddAsync(new Series { Title = "Fünf Freunde" });

            IServiceScopeFactory localFactory = BuildLocalScopeFactory(localData);

            List<ImportSeries> onlineResults =
            [
                new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "tkkg-1" }
            ];

            // Default-Scope 0 = Alle Quellen
            SucheViewModel vm = BuildViewModel(onlineResults, scopeFactory: localFactory);
            vm.SearchText = "e"; // trifft "Fünf Freunde" lokal; Online gibt immer seine feste Liste zurück
            vm.SearchCommand.Execute(null);
            await vm.WaitForSearchCompleteAsync();

            // TKKG (online) + Fünf Freunde (lokal) = 2 Ergebnisse
            Assert.Equal(2, vm.Results.Count);
        }

        /// <summary>
        /// Hilfsklasse: wirft immer eine Exception, um Netzwerkfehler zu simulieren.
        /// </summary>
        private sealed class ThrowingSeriesImportSearch : ISeriesImportSearch
        {
            /// <inheritdoc/>
            public Task<IReadOnlyList<ImportSeries>> SearchAsync(string query) =>
                throw new InvalidOperationException("Simulierter Netzwerkfehler");
        }
    }
}
