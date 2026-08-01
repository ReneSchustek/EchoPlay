using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Dieselbe Folge kann zweimal in der Bibliothek stehen — einmal lokal eingelesen, einmal
    /// vom Anbieter importiert. „Gehört" muss dann für beide gelten, sonst zeigt die andere
    /// Ansicht sie weiter als offen. In der Produktivdatenbank waren davon 904 Folgen betroffen.
    /// </summary>
    public sealed class PlaybackStateCrossSourceTests : DbTestBase
    {
        [Fact]
        public async Task MarkCompleted_SetztDasGegenstueckDerAnderenQuelleMit()
        {
            (Episode lokal, Episode online) = await ZweiQuellenAsync("TKKG", 1, "Die Jagd nach den Millionendieben");
            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(lokal.Id, new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc),
                cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? gegenstueck = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(gegenstueck);
            Assert.True(gegenstueck.IsCompleted);
        }

        [Fact]
        public async Task MarkCompleted_UeberträgtAuchBeiAbweichendemFolgentitel()
        {
            // Der Folgentitel darf nicht Teil der Zuordnung sein: Lokal und online benennen
            // dieselbe Folge unterschiedlich — in der Produktivbibliothek bei 0 von 925 Paaren
            // gleich. Mit Titelvergleich hätte der Abgleich nie gegriffen.
            Series lokaleSerie = await DataBuilder.PersistSeriesAsync("TKKG");
            Series onlineSerie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode lokal = await FolgeAsync(lokaleSerie, 1, "Die Jagd nach den Millionendieben");
            Episode online = await FolgeAsync(onlineSerie, 1, "Die Jagd nach den Millionendieben (Remastered)");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(lokal.Id, DateTime.UtcNow,
                cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? gegenstueck = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(gegenstueck);
            Assert.True(gegenstueck.IsCompleted);
        }

        [Fact]
        public async Task MarkCompleted_UeberträgtNichtBeiAbweichenderFolgennummer()
        {
            Series lokaleSerie = await DataBuilder.PersistSeriesAsync("TKKG");
            Series onlineSerie = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode lokal = await FolgeAsync(lokaleSerie, 1, "Gleicher Titel");
            Episode online = await FolgeAsync(onlineSerie, 2, "Gleicher Titel");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(lokal.Id, DateTime.UtcNow,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task MarkNotStarted_SetztAuchDasGegenstueckZurueck()
        {
            // Gegenrichtung: Ohne sie würde der Abgleich wiederherstellen, was der Nutzer
            // gerade zurückgenommen hat.
            (Episode lokal, Episode online) = await ZweiQuellenAsync("Fünf Freunde", 7, "und der Zirkus");

            // Beide Stände direkt setzen, nicht über MarkCompletedAsync: Sonst besteht der Test
            // auch ohne die Spiegelung im Zurücksetzen, weil das Gegenstück gar nie gesetzt war.
            // Genau das hat die Gegenprobe aufgedeckt.
            foreach (Guid episodeId in new[] { lokal.Id, online.Id })
            {
                PlaybackState vorhanden = new() { EpisodeId = episodeId };
                vorhanden.MarkCompleted(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));
                _ = Context.PlaybackStates.Add(vorhanden);
            }

            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkNotStartedAsync(lokal.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await service.GetByEpisodeIdAsync(
                lokal.Id, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Synchronize_HoltDenAltbestandNachUndIstDanachStill()
        {
            // Altfall: lokal gehört, online offen — der Zustand, den 904 Folgen hatten, bevor
            // die Spiegelung existierte.
            (Episode lokal, Episode online) = await ZweiQuellenAsync("TKKG Junior", 3, "Der Hund von Baskerville");

            PlaybackState alt = new() { EpisodeId = lokal.Id };
            alt.MarkCompleted(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));
            _ = Context.PlaybackStates.Add(alt);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            int ersterLauf = await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            int zweiterLauf = await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, ersterLauf);

            // Idempotenz ist der Grund, warum der Abgleich ohne Einmal-Schalter und ohne
            // Migration auskommt: Er darf bei jedem Start laufen.
            Assert.Equal(0, zweiterLauf);

            PlaybackState? nachher = await service.GetByEpisodeIdAsync(
                online.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(nachher);
            Assert.True(nachher.IsCompleted);
        }

        [Fact]
        public async Task Synchronize_OhneRueckstandAendertNichts()
        {
            _ = await ZweiQuellenAsync("Pumuckl", 1, "Meister Eder und sein Pumuckl");
            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            Assert.Equal(0, await service.SynchronizeCompletionAcrossSourcesAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Legt dieselbe Folge zweimal an — in zwei Serien mit identischem Titel, wie es beim
        /// Nebeneinander von lokalem Einlesen und Anbieter-Import entsteht.
        /// </summary>
        private async Task<(Episode Lokal, Episode Online)> ZweiQuellenAsync(string serie, int nummer, string folge)
        {
            Series lokaleSerie = await DataBuilder.PersistSeriesAsync(serie);
            Series onlineSerie = await DataBuilder.PersistSeriesAsync(serie);

            return (await FolgeAsync(lokaleSerie, nummer, folge), await FolgeAsync(onlineSerie, nummer, folge));
        }

        /// <summary>
        /// Persistiert eine Folge mit Nummer — der Builder setzt nur den Titel, der Abgleich
        /// braucht aber beides.
        /// </summary>
        private async Task<Episode> FolgeAsync(Series serie, int nummer, string titel)
        {
            Episode episode = new()
            {
                SeriesId = serie.Id,
                Title = titel,
                EpisodeNumber = nummer
            };

            _ = Context.Episodes.Add(episode);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return episode;
        }
    }
}
