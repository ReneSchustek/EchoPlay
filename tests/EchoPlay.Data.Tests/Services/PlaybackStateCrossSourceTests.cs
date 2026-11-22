using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Dieselbe Folge kann zweimal in der Bibliothek stehen: einmal lokal eingelesen, einmal
    /// vom Anbieter importiert. „Gehört" muss dann für beide gelten, sonst zeigt die andere
    /// Ansicht sie weiter als offen. In der Produktivdatenbank waren davon 904 Folgen betroffen.
    /// </summary>
    public sealed class PlaybackStateCrossSourceTests : DbTestBase
    {
        [Fact]
        public async Task MarkCompleted_AlsoSetsTheCounterpartOfTheOtherSource()
        {
            (Episode local, Episode online) = await TwoSourcesAsync("TKKG", 1, "Die Jagd nach den Millionendieben");
            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(local.Id, new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc),
                cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? counterpart = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(counterpart);
            Assert.True(counterpart.IsCompleted);
        }

        [Fact]
        public async Task MarkCompleted_TransfersWithDifferentEpisodeTitle()
        {
            // Der Folgentitel darf nicht Teil der Zuordnung sein: Lokal und online benennen
            // dieselbe Folge unterschiedlich, in der Produktivbibliothek bei 0 von 925 Paaren
            // gleich. Mit Titelvergleich hätte der Abgleich nie gegriffen.
            Series localSeries = await DataBuilder.PersistSeriesAsync("TKKG");
            Series onlineSeries = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode local = await EpisodeAsync(localSeries, 1, "Die Jagd nach den Millionendieben");
            Episode online = await EpisodeAsync(onlineSeries, 1, "Die Jagd nach den Millionendieben (Remastered)");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(local.Id, DateTime.UtcNow,
                cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? counterpart = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(counterpart);
            Assert.True(counterpart.IsCompleted);
        }

        [Fact]
        public async Task MarkCompleted_DoesNotTransferWithDifferentEpisodeNumber()
        {
            Series localSeries = await DataBuilder.PersistSeriesAsync("TKKG");
            Series onlineSeries = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode local = await EpisodeAsync(localSeries, 1, "Gleicher Titel");
            Episode online = await EpisodeAsync(onlineSeries, 2, "Gleicher Titel");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(local.Id, DateTime.UtcNow,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task MarkNotStarted_AlsoResetsTheCounterpart()
        {
            // Gegenrichtung: Ohne sie würde der Abgleich wiederherstellen, was der Nutzer
            // gerade zurückgenommen hat.
            (Episode local, Episode online) = await TwoSourcesAsync("Fünf Freunde", 7, "und der Zirkus");

            // Beide Stände direkt setzen, nicht über MarkCompletedAsync: Sonst besteht der Test
            // auch ohne die Spiegelung im Zurücksetzen, weil das Gegenstück gar nie gesetzt war.
            // Genau das hat die Gegenprobe aufgedeckt.
            foreach (Guid episodeId in new[] { local.Id, online.Id })
            {
                PlaybackState existing = new() { EpisodeId = episodeId };
                existing.MarkCompleted(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));
                _ = Context.PlaybackStates.Add(existing);
            }

            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkNotStartedAsync(local.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await service.GetByEpisodeIdAsync(
                local.Id, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Synchronize_CatchesUpTheBacklogAndIsQuietAfterwards()
        {
            // Altfall: lokal gehört, online offen. Das ist der Zustand, den 904 Folgen hatten, bevor
            // die Spiegelung existierte.
            (Episode local, Episode online) = await TwoSourcesAsync("TKKG Junior", 3, "Der Hund von Baskerville");

            PlaybackState alt = new() { EpisodeId = local.Id };
            alt.MarkCompleted(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));
            _ = Context.PlaybackStates.Add(alt);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            int firstRun = await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            int secondRun = await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, firstRun);

            // Idempotenz ist der Grund, warum der Abgleich ohne Einmal-Schalter und ohne
            // Migration auskommt: Er darf bei jedem Start laufen.
            Assert.Equal(0, secondRun);

            PlaybackState? after = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(after);
            Assert.True(after.IsCompleted);
        }

        [Fact]
        public async Task Synchronize_WithoutBacklog_ChangesNothing()
        {
            _ = await TwoSourcesAsync("Pumuckl", 1, "Meister Eder und sein Pumuckl");
            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            Assert.Equal(0, await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Legt dieselbe Folge zweimal an, in zwei Serien mit identischem Titel, wie es beim
        /// Nebeneinander von lokalem Einlesen und Anbieter-Import entsteht.
        /// </summary>
        private async Task<(Episode Lokal, Episode Online)> TwoSourcesAsync(string series, int number, string storedEpisode)
        {
            Series localSeries = await DataBuilder.PersistSeriesAsync(series);
            Series onlineSeries = await DataBuilder.PersistSeriesAsync(series);

            return (await EpisodeAsync(localSeries, number, storedEpisode), await EpisodeAsync(onlineSeries, number, storedEpisode));
        }

        /// <summary>
        /// Persistiert eine Folge mit Nummer. Der Builder setzt nur den Titel, der Abgleich
        /// braucht aber beides.
        /// </summary>
        private async Task<Episode> EpisodeAsync(Series series, int number, string title)
        {
            Episode episode = new()
            {
                SeriesId = series.Id,
                Title = title,
                EpisodeNumber = number
            };

            _ = Context.Episodes.Add(episode);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return episode;
        }
    }
}
