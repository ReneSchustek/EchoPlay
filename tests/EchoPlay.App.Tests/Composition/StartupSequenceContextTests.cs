using EchoPlay.App.Services;
using EchoPlay.App.Startup;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Xunit;

namespace EchoPlay.App.Tests.Composition
{
    /// <summary>
    /// Prüft, dass der Startlauf seine neun Dienste vollständig aus dem fertig gebauten
    /// Container bekommt.
    /// </summary>
    /// <remarks>
    /// Der Startlauf holt sich nichts selbst — er bekommt alles aufgelöst gereicht. Fehlt
    /// eine Registrierung, fällt das ohne diesen Test erst beim Programmstart auf, und
    /// zwar mit einem Fenster, das gar nicht erst erscheint.
    /// </remarks>
    public sealed class StartupSequenceContextTests
    {
        [Fact]
        public void From_WithoutServices_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => StartupSequenceContext.From(null!));
        }

        [Fact]
        public void From_WithARegisteredContainer_FillsEveryMember()
        {
            using ServiceProvider provider = BuildProvider();

            StartupSequenceContext context = StartupSequenceContext.From(provider);

            Assert.NotNull(context.ScopeFactory);
            Assert.NotNull(context.LoggerManager);
            Assert.NotNull(context.LanguageSwitchService);
            Assert.NotNull(context.Clock);
            Assert.NotNull(context.ProviderIdService);
            Assert.NotNull(context.ThemeService);
            Assert.NotNull(context.Validator);
            Assert.NotNull(context.UpdateInteraction);
            Assert.NotNull(context.PlayerService);
        }

        [Fact]
        public void From_CalledTwice_ReturnsTheSameSingletons()
        {
            using ServiceProvider provider = BuildProvider();

            StartupSequenceContext first = StartupSequenceContext.From(provider);
            StartupSequenceContext second = StartupSequenceContext.From(provider);

            // Der Startlauf und die laufende Anwendung müssen auf denselben Diensten
            // arbeiten — ein zweites Farbschema oder ein zweites Abspielgerät wären
            // stille Fehlerquellen.
            Assert.Same(first.ThemeService, second.ThemeService);
            Assert.Same(first.PlayerService, second.PlayerService);
        }

        private static ServiceProvider BuildProvider()
        {
            string logDirectory = Path.Combine(Path.GetTempPath(), "echoplay-startup-context");
            LoggerOptions loggerOptions = new()
            {
                LogDirectory = logDirectory,
                EnableFileLogging = false,
                EnableAutoCleanup = false,
            };

            ServiceCollection services = new();
            _ = services.AddHttpClient();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
            _ = services.AddSingleton<EchoPlay.App.Services.CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<EchoPlay.App.Services.CoverService>());
            _ = services.AddSingleton<EpisodeCoverCacheService>();
            _ = services.AddSingleton<ISpotifyCredentialStore>(new FakeSpotifyCredentialStore());
            _ = services.AddSingleton(new BackgroundCoverServiceOptions());
            _ = services.AddSingleton<BackgroundCoverService>();
            _ = services.AddSingleton<IHostRateLimiter>(new RecordingHostRateLimiter());
            _ = services.AddSingleton<IInstallerLauncher>(new FakeInstallerLauncher());
            _ = services.AddSingleton<IPlayerService>(new FakePlayerService());
            _ = services.AddSingleton<ILanguageSwitchService>(new FakeLanguageSwitchService());

            _ = services.AddSingleton(new LoggerManager(
                new LoggerFactory([], loggerOptions), new LogCleanupService(loggerOptions), loggerOptions));

            _ = services.AddSingleton<BackgroundProviderIdService>();
            _ = services.AddSingleton<ThemeService>();
            _ = services.AddSingleton<IStartupValidator, StartupValidator>();
            _ = services.AddSingleton<UpdateCheckService>();
            _ = services.AddSingleton<UpdateDownloadService>();
            _ = services.AddSingleton<UpdateInteractionService>();

            return services.BuildServiceProvider();
        }
    }
}
