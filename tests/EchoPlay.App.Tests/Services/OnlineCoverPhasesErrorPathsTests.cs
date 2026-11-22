using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Was der Nachtrag fehlender Cover-Adressen tut, wenn der Anbieter nicht liefert.
    /// </summary>
    /// <remarks>
    /// Die Fälle sehen im Quelltext ähnlich aus und müssen sich trotzdem unterschiedlich
    /// verhalten: Ein Anbieterfehler und eine Zeitüberschreitung betreffen eine Serie und
    /// dürfen den Lauf über die übrigen nicht beenden. Ein Abbruch des Aufrufers betrifft den
    /// ganzen Lauf. Würde er wie ein Serienfehler behandelt, liefe die Schleife nach dem
    /// Schließen des Fensters weiter, bis zufällig die nächste Abbruchprüfung greift.
    /// </remarks>
    public sealed class OnlineCoverPhasesErrorPathsTests
    {
        private const string AppleArtistId = "201306317";
        private const string EpisodeTitle = "Folge 001 - Der Super-Papagei";

        /// <summary>
        /// Eine Importquelle, die mitten in der Abfrage den Lauf abbricht, so wie das Schließen
        /// des Fensters es tut, und dann den Abbruch meldet.
        /// </summary>
        private sealed class CancellingImportSource(CancellationTokenSource run) : IEpisodeImportSource
        {
            public async Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
                string sourceSeriesId,
                IReadOnlySet<string>? knownEpisodeTitles = null,
                CancellationToken cancellationToken = default)
            {
                await run.CancelAsync();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        /// <summary>
        /// Eine Importquelle, deren Anfrage an der Zeitgrenze des HTTP-Clients scheitert. Der
        /// Client meldet das als <see cref="TaskCanceledException"/>, ohne dass der Aufrufer
        /// abgebrochen hätte.
        /// </summary>
        private sealed class TimingOutImportSource : IEpisodeImportSource
        {
            public Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
                string sourceSeriesId,
                IReadOnlySet<string>? knownEpisodeTitles = null,
                CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<ImportEpisode>>(new TaskCanceledException("Zeitgrenze erreicht"));
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_WhenTheProviderFails_LeavesTheEpisodeUntouched()
        {
            (FakeSeriesDataService seriesService, FakeEpisodeDataService episodeService, Episode episode) =
                await SeedAsync();

            OnlineCoverPhases phases = BuildPhases(
                seriesService,
                episodeService,
                new FakeEpisodeImportSource([EpisodeFromProvider()], failForSourceSeriesId: AppleArtistId));

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            // Kein Eintrag, keine Ausnahme: Die Folge behält ihren Zustand, der Lauf endet sauber.
            Assert.Equal(0, updated);
            Assert.True(string.IsNullOrEmpty(episode.CoverImageUrl));
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_WhenTheRunIsCancelled_StopsInsteadOfWarning()
        {
            (FakeSeriesDataService seriesService, FakeEpisodeDataService episodeService, _) = await SeedAsync();
            using CancellationTokenSource run = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new CancellingImportSource(run));

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => phases.UpdateMissingCoverUrlsAsync(run.Token));
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_WhenTheProviderTimesOut_TreatsItAsASeriesFailure()
        {
            (FakeSeriesDataService seriesService, FakeEpisodeDataService episodeService, Episode episode) =
                await SeedAsync();

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new TimingOutImportSource());

            // Die Zeitüberschreitung trifft nur diese Serie: kein Wurf, der den Lauf über die
            // übrigen Serien beenden würde.
            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, updated);
            Assert.True(string.IsNullOrEmpty(episode.CoverImageUrl));
        }

        /// <summary>
        /// Eine online importierte Serie mit genau einer Folge ohne Cover-Adresse als
        /// Ausgangszustand, den der Nachtrag verbessern soll.
        /// </summary>
        private static async Task<(FakeSeriesDataService Series, FakeEpisodeDataService Episodes, Episode Episode)> SeedAsync()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new()
            {
                Title = "Die drei Fragezeichen",
                IsOnlineImported = true,
                AppleMusicArtistId = AppleArtistId,
            };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            return (seriesService, episodeService, episode);
        }

        private static OnlineCoverPhases BuildPhases(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            IEpisodeImportSource appleSource)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddKeyedScoped(ProviderKeys.AppleMusic, (_, _) => appleSource);

            ServiceProvider provider = services.BuildServiceProvider();

            return new OnlineCoverPhases(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverService(),
                new FakeCoverDownloader(),
                new FakeSpotifyCredentialStore(),
                                new FakeLogger());
        }

        private static ImportEpisode EpisodeFromProvider() => new()
        {
            SourceEpisodeId = "provider-folge-001",
            Title = EpisodeTitle,
            CoverImageUrl = new Uri("https://is1.mzstatic.com/image/apple.jpg"),
        };
    }
}
