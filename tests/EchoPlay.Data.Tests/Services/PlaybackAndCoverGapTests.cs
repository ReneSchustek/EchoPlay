using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Projections;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Prüft die Wege rund um Hörstände und Cover, die sonst nur die Oberfläche
    /// benutzt.
    /// </summary>
    /// <remarks>
    /// Der Hörstand ist der einzige Wert, den der Anwender nicht wiederherstellen kann,
    /// wenn er verloren geht. Zwei Bereiche der Anwendung schreiben ihn parallel: der
    /// Startlauf und die Wiedergabe. Der eindeutige Index fängt das ab; für den Aufrufer
    /// muss das lautlos gutgehen und darf nicht als Fehler ankommen.
    /// </remarks>
    public sealed class PlaybackAndCoverGapTests : DbTestBase
    {
        [Fact]
        public async Task Playback_AddingTheSameEpisodeTwice_KeepsTheFirstEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            await sut.AddAsync(
                new PlaybackState { EpisodeId = episode.Id, LastPosition = TimeSpan.FromMinutes(5) },
                TestContext.Current.CancellationToken);

            Context.ChangeTracker.Clear();

            await sut.AddAsync(
                new PlaybackState { EpisodeId = episode.Id, LastPosition = TimeSpan.FromMinutes(1) },
                TestContext.Current.CancellationToken);

            // Der zweite Eintrag verliert. Ein doppelter Hörstand für dieselbe Folge
            // hieße, dass die Anwendung nicht mehr weiß, wo der Anwender stehen geblieben ist.
            PlaybackState einziger = Assert.Single(
                await Context.PlaybackStates.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(TimeSpan.FromMinutes(5), einziger.LastPosition);
        }

        [Fact]
        public async Task Playback_CompletedIdsForNoEpisodes_AreEmpty()
        {
            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            HashSet<Guid> fertige = await sut.GetCompletedEpisodeIdsAsync(
                [], TestContext.Current.CancellationToken);

            Assert.Empty(fertige);
        }

        [Fact]
        public async Task Playback_CompletedIds_NameOnlyTheFinishedEpisodes()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode done = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Episode open = await DataBuilder.PersistEpisodeAsync(series, "Folge 2");

            Context.PlaybackStates.AddRange(
                new PlaybackState { EpisodeId = done.Id, IsCompleted = true },
                new PlaybackState { EpisodeId = open.Id, IsCompleted = false });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            HashSet<Guid> fertige = await sut.GetCompletedEpisodeIdsAsync(
                [done.Id, open.Id], TestContext.Current.CancellationToken);

            // Das Häkchen auf der Kachel hängt daran. Eine Folge fälschlich als gehört
            // zu zeigen, ist schlimmer als gar kein Häkchen.
            Guid einzig = Assert.Single(fertige);
            Assert.Equal(done.Id, einzig);
        }

        [Fact]
        public async Task Playback_DeletingAnUnknownState_ChangesNothing()
        {
            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            await sut.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

            Assert.Empty(await Context.PlaybackStates.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public void RecentPlaybackRow_CarriesTheValuesForTheDashboard()
        {
            DateTime sorted = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
            RecentPlaybackRow row = new(
                Guid.NewGuid(), Guid.NewGuid(), IsCompleted: false, TimeSpan.FromMinutes(7), sorted);

            // Die schmale Projektion spart das Laden der vollen Entität samt Prüffeldern.
            // Sie muss trotzdem alles tragen, was die Kachel anzeigt.
            Assert.False(row.IsCompleted);
            Assert.Equal(TimeSpan.FromMinutes(7), row.LastPosition);
            Assert.Equal(sorted, row.SortKey);
        }

        [Fact]
        public async Task Covers_AreCounted()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CoverImageDataService sut = new(Context, NullLoggerFactory);

            await sut.SetCoverAsync("Series", series.Id, [0x01, 0x02], null, TestContext.Current.CancellationToken);

            Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Covers_UpsertingTwice_KeepsOneEntryWithTheNewImage()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CoverImageDataService sut = new(Context, NullLoggerFactory);

            await sut.SetCoverAsync("Series", series.Id, [0x01], new Uri("https://alt.invalid/a.jpg"), TestContext.Current.CancellationToken);
            await sut.SetCoverAsync("Series", series.Id, [0x02], new Uri("https://neu.invalid/b.jpg"), TestContext.Current.CancellationToken);

            // Cover werden über den Hintergrundlauf immer wieder nachgezogen. Jedes Mal
            // eine neue Zeile hieße, dass die Datenbank mit jedem Start weiter wächst.
            CoverImage einziges = Assert.Single(
                await Context.CoverImages.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal([0x02], einziges.ImageData);
        }

        [Fact]
        public void DesignTimeFactory_BuildsAContext()
        {
            // Die Fabrik existiert allein für die Werkzeuge, die Migrationen erzeugen.
            // Wirft sie, lässt sich keine neue Migration mehr anlegen.
            using EchoPlayDbContext context = new EchoPlayDbContextFactory().CreateDbContext([]);

            Assert.NotNull(context);
        }
    }
}
