using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Baut den <see cref="SyncService"/> mit Nachbauten für Daten, Scan und Cover. Die Testklassen
    /// des Abgleichs setzen nur, was ihr Fall braucht; alles andere startet leer.
    /// </summary>
    internal sealed class SyncServiceSetup
    {
        public required FakeAppSettingsDataService Settings { get; init; }

        public FakeSeriesDataService Series { get; init; } = new();

        public FakeEpisodeDataService Episodes { get; init; } = new();

        public FakeLocalTrackDataService Tracks { get; init; } = new();

        public FakeLocalLibraryScanner Scanner { get; init; } = new([]);

        public FakeLocalCoverService LocalCovers { get; init; } = new();

        public FakeTrackMatcher TrackMatcher { get; init; } = new();

        public FakeAudioMetadataReader MetadataReader { get; init; } = new();

        public FakeCoverImageDataService CoverImages { get; init; } = new();

        public FakeScanEventService ScanEvents { get; init; } = new();

        /// <summary>
        /// Baut den Dienst. Ohne <paramref name="coverService"/> arbeitet er mit dem echten
        /// Cover-Dienst über dieselben Nachbauten.
        /// </summary>
        public SyncService Build(ICoverService? coverService = null)
        {
            ServiceProvider provider = BuildProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            ILoggerFactory loggerFactory = provider.GetRequiredService<ILoggerFactory>();

            return new SyncService(
                scopeFactory,
                loggerFactory,
                ScanEvents,
                coverService ?? new EchoPlay.App.Services.CoverService(scopeFactory, loggerFactory));
        }

        /// <summary>
        /// Registriert alle Dienste, die der Abgleich je Bereich auflöst. Der Kopierdienst gehört
        /// dazu: Fehlte er, bräche der Cover-Schritt nach dem Scan still ab.
        /// </summary>
        private ServiceProvider BuildProvider()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => Settings);
            _ = services.AddScoped<ISeriesDataService>(_ => Series);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => Episodes);
            _ = services.AddScoped<ILocalTrackDataService>(_ => Tracks);
            _ = services.AddScoped<ILocalLibraryScanner>(_ => Scanner);
            _ = services.AddScoped<IScanOrchestrator>(_ => new FakeScanOrchestrator(Scanner));
            _ = services.AddScoped<ILocalCoverService>(_ => LocalCovers);
            _ = services.AddScoped<ITrackMatcher>(_ => TrackMatcher);
            _ = services.AddScoped<IAudioMetadataReader>(_ => MetadataReader);
            _ = services.AddScoped<ICoverImageDataService>(_ => CoverImages);
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            return services.BuildServiceProvider();
        }
    }
}
