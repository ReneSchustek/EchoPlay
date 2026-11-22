using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Registriert, was der <see cref="ImportService"/> in den Tests der Online-Seiten braucht:
    /// die Nachbauten beider Anbieter und die Cover-Kette bis zum Folgen-Cover-Cache. Die
    /// Datendienste registriert jede Testklasse selbst, weil sie dort den Unterschied macht.
    /// </summary>
    internal static class ImportTestServices
    {
        /// <summary>
        /// Registriert Suche und Folgenquelle beider Anbieter. Ohne Angabe liefern sie nichts.
        /// </summary>
        public static void AddProviderFakes(
            ServiceCollection services,
            IReadOnlyList<ImportSeries>? spotifyResults = null,
            ISeriesImportSearch? spotifySearch = null,
            ISeriesImportSearch? appleMusicSearch = null,
            IEpisodeImportSource? episodeSource = null)
        {
            _ = services.AddKeyedScoped<ISeriesImportSearch>(
                "Spotify", (_, _) => spotifySearch ?? new FakeSeriesImportSearch(spotifyResults ?? [], "Spotify"));
            _ = services.AddKeyedScoped<ISeriesImportSearch>(
                "AppleMusic", (_, _) => appleMusicSearch ?? new FakeSeriesImportSearch([], "AppleMusic"));
            _ = services.AddKeyedScoped<IEpisodeImportSource>(
                "Spotify", (_, _) => episodeSource ?? new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<IEpisodeImportSource>(
                "AppleMusic", (_, _) => episodeSource ?? new FakeEpisodeImportSource([]));
        }

        /// <summary>
        /// Registriert Protokoll, Uhr, HTTP-Fabrik und die Cover-Kette. Der Downloader ist ohne
        /// Angabe ein Nachbau, der nie ins Netz geht.
        /// </summary>
        public static void AddCoverChain(ServiceCollection services, ICoverDownloader? downloader = null)
        {
            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton(downloader ?? new FakeCoverDownloader());
            _ = services.AddSingleton<EpisodeCoverCacheService>();
        }

        /// <summary>Baut den Importdienst aus einem Provider, in dem die Cover-Kette steht.</summary>
        public static ImportService CreateImportService(ServiceProvider provider)
            => new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<ILoggerFactory>());
    }
}
