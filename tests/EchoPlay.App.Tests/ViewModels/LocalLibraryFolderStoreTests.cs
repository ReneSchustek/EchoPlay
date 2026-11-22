using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was die Ordnerwahl der lokalen Mediathek in Einstellungen und Bestand schreibt.
    /// </summary>
    public sealed class LocalLibraryFolderStoreTests : IDisposable
    {
        private readonly AppSettings _settings = new();
        private readonly FakeAppSettingsDataService _settingsService;
        private readonly FakeSeriesDataService _seriesService = new();
        private readonly ServiceProvider _provider;
        private readonly LocalLibraryFolderStore _store;

        /// <summary>Baut den Speicher über einen Container mit gefälschter Datenhaltung.</summary>
        public LocalLibraryFolderStoreTests()
        {
            _settingsService = new FakeAppSettingsDataService(_settings);
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => _settingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => _seriesService);
            _provider = services.BuildServiceProvider();
            _store = new LocalLibraryFolderStore(_provider.GetRequiredService<IServiceScopeFactory>());
        }

        /// <inheritdoc/>
        public void Dispose() => _provider.Dispose();

        [Fact]
        public async Task SaveRootPathAsync_WritesThePathIntoTheSettings()
        {
            await _store.SaveRootPathAsync(@"D:\Hörspiele");

            Assert.Equal(@"D:\Hörspiele", _settings.LocalLibraryRootPath);
            Assert.Equal(1, _settingsService.SaveCallCount);
        }

        [Fact]
        public async Task TryAddSeriesFolderAsync_NewFolder_AddsALocalSeries()
        {
            bool added = await _store.TryAddSeriesFolderAsync("Abenteuer", @"D:\Hörspiele\Abenteuer");

            Assert.True(added);
            Series series = Assert.Single(_seriesService.All);
            Assert.Equal("Abenteuer", series.Title);
            Assert.Equal(@"D:\Hörspiele\Abenteuer", series.LocalFolderPath);
        }

        [Fact]
        public async Task TryAddSeriesFolderAsync_FolderAlreadyKnown_AddsNothing()
        {
            _ = await _store.TryAddSeriesFolderAsync("Abenteuer", @"D:\Hörspiele\Abenteuer");

            bool added = await _store.TryAddSeriesFolderAsync("abenteuer", @"d:\hörspiele\abenteuer");

            Assert.False(added);
            _ = Assert.Single(_seriesService.All);
        }

        [Fact]
        public async Task IsLibraryEmptyAsync_FollowsTheStock()
        {
            Assert.True(await _store.IsLibraryEmptyAsync());

            _ = await _store.TryAddSeriesFolderAsync("Abenteuer", @"D:\Hörspiele\Abenteuer");

            Assert.False(await _store.IsLibraryEmptyAsync());
        }
    }
}
