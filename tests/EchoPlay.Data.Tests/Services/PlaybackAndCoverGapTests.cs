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
    /// Prüft die Wege rund um Hörstände und Cover, die bisher nur von der Oberfläche
    /// aus benutzt wurden.
    /// </summary>
    /// <remarks>
    /// Der Hörstand ist der einzige Wert, den der Anwender nicht wiederherstellen kann,
    /// wenn er verloren geht. Zwei Bereiche der Anwendung schreiben ihn parallel — der
    /// Startlauf und die Wiedergabe. Der eindeutige Index fängt das ab; für den Aufrufer
    /// muss das lautlos gutgehen und darf nicht als Fehler ankommen.
    /// </remarks>
    public sealed class PlaybackAndCoverGapTests : DbTestBase
    {
        [Fact]
        public async Task Playback_AddingTheSameEpisodeTwice_KeepsTheFirstEntry()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode folge = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");

            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            await sut.AddAsync(
                new PlaybackState { EpisodeId = folge.Id, LastPosition = TimeSpan.FromMinutes(5) },
                TestContext.Current.CancellationToken);

            Context.ChangeTracker.Clear();

            await sut.AddAsync(
                new PlaybackState { EpisodeId = folge.Id, LastPosition = TimeSpan.FromMinutes(1) },
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
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode fertig = await DataBuilder.PersistEpisodeAsync(serie, "Folge 1");
            Episode offen = await DataBuilder.PersistEpisodeAsync(serie, "Folge 2");

            Context.PlaybackStates.AddRange(
                new PlaybackState { EpisodeId = fertig.Id, IsCompleted = true },
                new PlaybackState { EpisodeId = offen.Id, IsCompleted = false });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService sut = new(Context, NullLoggerFactory);

            HashSet<Guid> fertige = await sut.GetCompletedEpisodeIdsAsync(
                [fertig.Id, offen.Id], TestContext.Current.CancellationToken);

            // Das Häkchen auf der Kachel hängt daran. Eine Folge fälschlich als gehört
            // zu zeigen, ist schlimmer als gar kein Häkchen.
            Guid einzig = Assert.Single(fertige);
            Assert.Equal(fertig.Id, einzig);
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
            DateTime sortiert = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
            RecentPlaybackRow zeile = new(
                Guid.NewGuid(), Guid.NewGuid(), IsCompleted: false, TimeSpan.FromMinutes(7), sortiert);

            // Die schmale Projektion spart das Laden der vollen Entität samt Prüffeldern.
            // Sie muss trotzdem alles tragen, was die Kachel anzeigt.
            Assert.False(zeile.IsCompleted);
            Assert.Equal(TimeSpan.FromMinutes(7), zeile.LastPosition);
            Assert.Equal(sortiert, zeile.SortKey);
        }

        [Fact]
        public async Task Covers_AreCounted()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            CoverImageDataService sut = new(Context, NullLoggerFactory);

            await sut.SetCoverAsync("Series", serie.Id, [0x01, 0x02], null, TestContext.Current.CancellationToken);

            Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Covers_UpsertingTwice_KeepsOneEntryWithTheNewImage()
        {
            Series serie = await DataBuilder.PersistSeriesAsync("TKKG");
            CoverImageDataService sut = new(Context, NullLoggerFactory);

            await sut.SetCoverAsync("Series", serie.Id, [0x01], "https://alt.invalid/a.jpg", TestContext.Current.CancellationToken);
            await sut.SetCoverAsync("Series", serie.Id, [0x02], "https://neu.invalid/b.jpg", TestContext.Current.CancellationToken);

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
            using EchoPlayDbContext kontext = new EchoPlayDbContextFactory().CreateDbContext([]);

            Assert.NotNull(kontext);
        }
    }
}
