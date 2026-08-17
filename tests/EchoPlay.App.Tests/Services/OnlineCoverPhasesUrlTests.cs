using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Nachtrag fehlender Cover-Adressen bei den Anbietern.
    /// </summary>
    /// <remarks>
    /// Die Weiche zwischen Spotify und Apple Music ist der heikle Teil: Spotify braucht
    /// hinterlegte Zugangsdaten, und ohne sie scheitert schon die Anmeldung. Eine Serie, die
    /// beide Kennungen trägt, muss deshalb auf Apple Music ausweichen statt in einen
    /// aussichtslosen Aufruf zu laufen.
    /// </remarks>
    public sealed class OnlineCoverPhasesUrlTests
    {
        private const string SpotifyArtistId = "4tZwfgrHOc3mvqYlEYSvVi";
        private const string AppleArtistId = "201306317";
        private const string EpisodeTitle = "Folge 001 - Der Super-Papagei";
        private const string SpotifyCoverUrl = "https://i.scdn.co/image/spotify.jpg";
        private const string AppleCoverUrl = "https://is1.mzstatic.com/image/apple.jpg";

        [Fact]
        public async Task UpdateMissingCoverUrls_WithoutSpotifyCredentials_FallsBackToAppleMusic()
        {
            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            // Kein Zugang hinterlegt — Spotify scheidet aus, obwohl die Kennung da ist.
            FakeSpotifyCredentialStore credentials = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, credentials);

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, updated);
            Assert.Equal(AppleCoverUrl, episode.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_WithSpotifyCredentials_PrefersSpotify()
        {
            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeSpotifyCredentialStore credentials = new();
            await credentials.SaveAsync("kennung", "geheimnis", TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, credentials);

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, updated);
            Assert.Equal(SpotifyCoverUrl, episode.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_SeriesNotImportedFromAProvider_IsSkipped()
        {
            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            series.IsOnlineImported = false;
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new FakeSpotifyCredentialStore());

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            // Rein lokale Serien haben beim Anbieter nichts zu suchen.
            Assert.Equal(0, updated);
            Assert.Null(episode.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_SeriesWithoutAnyProviderId_IsSkipped()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Ohne Anbieterkennung", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new FakeSpotifyCredentialStore());

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, updated);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_EpisodeThatAlreadyHasAnUrl_IsLeftAlone()
        {
            const string existingUrl = "https://i.example.com/schon-da.jpg";

            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle, CoverImageUrl = existingUrl };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new FakeSpotifyCredentialStore());

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            // Eine vorhandene Adresse wird nicht überschrieben — sonst wandert ein von Hand
            // gesetztes Bild bei jedem Durchlauf zurück auf das des Anbieters.
            Assert.Equal(0, updated);
            Assert.Equal(existingUrl, episode.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_MatchesEpisodeTitleIgnoringCase()
        {
            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = EpisodeTitle.ToUpperInvariant() };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new FakeSpotifyCredentialStore());

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            // Die Schreibweise der Anbieter schwankt zwischen Abrufen. Wer hier genau
            // vergleicht, verliert die Zuordnung bei der nächsten Umbenennung.
            Assert.Equal(1, updated);
            Assert.Equal(AppleCoverUrl, episode.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateMissingCoverUrls_WhenProviderKnowsNoMatchingTitle_ChangesNothing()
        {
            FakeSeriesDataService seriesService = new();
            Series series = SeriesWithBothProviders();
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = "Eine Folge, die der Anbieter nicht kennt" };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService, new FakeSpotifyCredentialStore());

            int updated = await phases.UpdateMissingCoverUrlsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, updated);
            Assert.Null(episode.CoverImageUrl);
        }

        private static Series SeriesWithBothProviders()
        {
            return new Series
            {
                Title = "Die drei Fragezeichen",
                IsOnlineImported = true,
                SpotifyArtistId = SpotifyArtistId,
                AppleMusicArtistId = AppleArtistId,
            };
        }

        /// <summary>
        /// Baut die Phasen mit je einer Anbieter-Quelle unter ihrem Schlüssel. Die Klasse
        /// holt sie als benannten Dienst — welchen Schlüssel sie wählt, ist genau die
        /// Entscheidung, die hier geprüft wird.
        /// </summary>
        private static OnlineCoverPhases BuildPhases(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeSpotifyCredentialStore credentialStore)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);

            _ = services.AddKeyedScoped<IEpisodeImportSource>(
                ProviderKeys.Spotify,
                (_, _) => new FakeEpisodeImportSource([EpisodeFromProvider(SpotifyCoverUrl)]));

            _ = services.AddKeyedScoped<IEpisodeImportSource>(
                ProviderKeys.AppleMusic,
                (_, _) => new FakeEpisodeImportSource([EpisodeFromProvider(AppleCoverUrl)]));

            ServiceProvider provider = services.BuildServiceProvider();

            return new OnlineCoverPhases(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverService(),
                new FakeCoverDownloader(),
                credentialStore,
                new FakeClock(),
                rateLimiter: null,
                new FakeLogger());
        }

        private static ImportEpisode EpisodeFromProvider(string coverUrl)
        {
            return new ImportEpisode
            {
                SourceEpisodeId = "provider-folge-001",
                Title = EpisodeTitle,
                CoverImageUrl = coverUrl,
            };
        }
    }
}
