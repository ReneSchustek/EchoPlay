using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="AppSettingsDataService"/>.
    /// Prüft das Laden und Speichern der Singleton-Einstellungszeile.
    /// </summary>
    public sealed class AppSettingsTests : DbTestBase
    {
        [Fact]
        public async Task GetAsync_ReturnsDefaultSettings_WhenEmpty()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings settings = await service.GetAsync();

            Assert.NotNull(settings);
            Assert.Equal(90, settings.NewReleaseDays);
            Assert.False(settings.OfflineMode);
        }

        [Fact]
        public async Task SaveAsync_PersistsChanges()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings settings = await service.GetAsync();
            settings.NewReleaseDays = 30;
            settings.OfflineMode = true;
            await service.SaveAsync(settings);
            Context.ChangeTracker.Clear();

            AppSettings reloaded = await service.GetAsync();
            Assert.Equal(30, reloaded.NewReleaseDays);
            Assert.True(reloaded.OfflineMode);
        }

        [Fact]
        public async Task GetAsync_ReturnsSameInstance_OnRepeatedCalls()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings first = await service.GetAsync();
            AppSettings second = await service.GetAsync();

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task SaveAsync_ClearCacheOnNextStart_Roundtrip()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings settings = await service.GetAsync();
            settings.ClearCacheOnNextStart = true;
            await service.SaveAsync(settings);
            Context.ChangeTracker.Clear();

            AppSettings reloaded = await service.GetAsync();
            Assert.True(reloaded.ClearCacheOnNextStart);
        }
    }
}
