using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Was der Nachtrag fehlender Cover-Adressen tut, wenn der Anbieter nicht liefert.
    /// </summary>
    /// <remarks>
    /// Die beiden Fälle sehen im Quelltext ähnlich aus und müssen sich trotzdem
    /// unterschiedlich verhalten: Ein Anbieterfehler betrifft eine Serie und darf den Lauf
    /// über die übrigen nicht beenden. Ein Abbruch betrifft den ganzen Lauf — würde er wie
    /// ein Serienfehler behandelt, liefe die Schleife nach dem Schließen des Fensters
    /// weiter, bis zufällig die nächste Abbruchprüfung greift.
    /// </remarks>
    public sealed class OnlineCoverPhasesErrorPathsTests
    {
        private const string AppleArtistId = "201306317";
        private const string EpisodeTitle = "Folge 001 - Der Super-Papagei";

        /// <summary>Eine Importquelle, die den Abbruch der umgebenden Operation meldet.</summary>
        private sealed class CancellingImportSource : IEpisodeImportSource
        {
            public Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
                string sourceSeriesId,
                IReadOnlySet<string>? knownEpisodeTitles = null,
                CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<ImportEpisode>>(new OperationCanceledException());
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

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new CancellingImportSource());

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => phases.UpdateMissingCoverUrlsAsync(CancellationToken.None));
        }

        /// <summary>
        /// Eine online importierte Serie mit genau einer Folge ohne Cover-Adresse — der
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
                new FakeClock(),
                rateLimiter: null,
                new FakeLogger());
        }

        private static ImportEpisode EpisodeFromProvider() => new()
        {
            SourceEpisodeId = "provider-folge-001",
            Title = EpisodeTitle,
            CoverImageUrl = "https://is1.mzstatic.com/image/apple.jpg",
        };
    }
}
