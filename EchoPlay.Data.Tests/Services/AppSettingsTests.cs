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

            AppSettings settings = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(settings);
            Assert.Equal(90, settings.NewReleaseDays);
            Assert.False(settings.OfflineMode);
        }

        [Fact]
        public async Task SaveAsync_PersistsChanges()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings settings = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            settings.NewReleaseDays = 30;
            settings.OfflineMode = true;
            await service.SaveAsync(settings, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            AppSettings reloaded = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(30, reloaded.NewReleaseDays);
            Assert.True(reloaded.OfflineMode);
        }

        [Fact]
        public async Task GetAsync_ReturnsSameInstance_OnRepeatedCalls()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings first = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            AppSettings second = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task SaveAsync_ClearCacheOnNextStart_Roundtrip()
        {
            AppSettingsDataService service = new(Context, NullLoggerFactory);

            AppSettings settings = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            settings.ClearCacheOnNextStart = true;
            await service.SaveAsync(settings, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            AppSettings reloaded = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(reloaded.ClearCacheOnNextStart);
        }
    }
}
