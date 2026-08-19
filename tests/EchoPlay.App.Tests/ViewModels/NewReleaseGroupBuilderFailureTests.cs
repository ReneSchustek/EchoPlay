using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Neuerscheinungs-Abschnitt, wenn der Zwischenspeicher nicht lesbar ist.
    /// </summary>
    /// <remarks>
    /// Der Abschnitt ist einer von mehreren auf der Startseite. Reißt sein Aufbau ab,
    /// bleibt die ganze Seite leer — obwohl Mediathek, Favoriten und Weiterhören völlig
    /// unabhängig davon sind. Deshalb endet ein Fehler hier in einer leeren Liste und
    /// nicht in einer Ausnahme.
    /// </remarks>
    public sealed class NewReleaseGroupBuilderFailureTests
    {
        [Fact]
        public async Task Build_WhenTheEpisodeStoreIsMissing_ReturnsNoGroups()
        {
            Series series = new() { Title = "TKKG", IsWatched = true };
            NewReleaseGroupBuilder sut = Build(withEpisodeStore: false, CachedRelease(series.Id));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await sut.BuildAsync([series], TestContext.Current.CancellationToken);

            Assert.Empty(groups);
        }

        [Fact]
        public async Task Build_WhenTheEpisodeStoreIsUnreachable_ReturnsNoGroups()
        {
            Series series = new() { Title = "TKKG", IsWatched = true };
            NewReleaseGroupBuilder sut = Build(
                withEpisodeStore: true,
                storeFailure: new IOException("Datenbankdatei nicht erreichbar"),
                cached: CachedRelease(series.Id));

            // Eine Datenbank auf einem getrennten Netzlaufwerk sieht so aus. Die Startseite
            // zeigt dann eben keine Neuerscheinungen — mehr darf nicht passieren.
            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await sut.BuildAsync([series], TestContext.Current.CancellationToken);

            Assert.Empty(groups);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static CachedNewRelease CachedRelease(Guid seriesId)
            => new()
            {
                SeriesId = seriesId,
                Title = "TKKG - Folge 240",
                EpisodeNumber = 240,
                ReleaseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            };

        private static NewReleaseGroupBuilder Build(
            bool withEpisodeStore,
            params CachedNewRelease[] cached)
            => Build(withEpisodeStore, storeFailure: null, cached);

        private static NewReleaseGroupBuilder Build(
            bool withEpisodeStore,
            Exception? storeFailure,
            params CachedNewRelease[] cached)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICachedNewReleaseDataService>(
                _ => new FakeCachedNewReleaseDataService(cached));

            if (withEpisodeStore)
            {
                _ = services.AddScoped<IEpisodeDataService>(
                    _ => storeFailure is null
                        ? new FakeEpisodeDataService()
                        : throw storeFailure);
            }

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            DashboardCoverProvider coverProvider = new(
                scopeFactory, coverService: null, backgroundCoverService: null, dispatcherQueue: null);

            NewEpisodeCardServices cardServices = new(
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLocalizationService());

            return new NewReleaseGroupBuilder(
                scopeFactory, coverProvider, cardServices, new FakeClock(), new FakeLogger());
        }
    }
}
