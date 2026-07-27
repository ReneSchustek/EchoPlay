using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für die Merkliste überwachter Serientitel. Sie ist der einzige Zustand, der ein
    /// Leeren der Mediathek überlebt – ohne sie startet jede neu eingelesene Serie unbeobachtet.
    /// </summary>
    public sealed class WatchedTitleTests : DbTestBase
    {
        private WatchedTitleDataService CreateWatchedTitles() => new(Context, NullLoggerFactory);

        private SeriesDataService CreateSeries(WatchedTitleDataService watchedTitles) =>
            new(Context, watchedTitles, NullLoggerFactory);

        [Fact]
        public async Task SetWatched_Enabling_RemembersNormalizedTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Die drei ???");
            Context.ChangeTracker.Clear();

            WatchedTitleDataService watchedTitles = CreateWatchedTitles();
            await CreateSeries(watchedTitles)
                .SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlySet<string> titles =
                await watchedTitles.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("die drei ", titles);
        }

        [Fact]
        public async Task SetWatched_Disabling_ForgetsTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            WatchedTitleDataService watchedTitles = CreateWatchedTitles();
            SeriesDataService service = CreateSeries(watchedTitles);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);
            await service.SetWatchedAsync(series.Id, false, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlySet<string> titles =
                await watchedTitles.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(titles);
        }

        [Fact]
        public async Task SetFavorite_AlsoRemembersTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Fünf Freunde");
            Context.ChangeTracker.Clear();

            WatchedTitleDataService watchedTitles = CreateWatchedTitles();
            await CreateSeries(watchedTitles)
                .SetFavoriteAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlySet<string> titles =
                await watchedTitles.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Umlaut wird auf die ASCII-Vergleichsform abgebildet.
            Assert.Contains("fuenf freunde", titles);
        }

        [Fact]
        public async Task SetWatched_Twice_KeepsSingleEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = CreateSeries(CreateWatchedTitles());
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            // Der Unique-Index würde beim zweiten Insert werfen – die Prüfung muss vorher greifen.
            Assert.Equal(1, await Context.WatchedTitles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Remember_UnknownTitle_IsIgnored()
        {
            // Leere Titel dürfen keine Merklisten-Zeile erzeugen: die Normalisierung
            // würde sonst einen leeren Schlüssel gegen den Unique-Index schreiben.
            WatchedTitleDataService watchedTitles = CreateWatchedTitles();

            await watchedTitles.RememberAsync("   ", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, await Context.WatchedTitles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SyncFromWatchedSeries_AddsMissingEntriesForWatchedSeries()
        {
            // Altbestand: IsWatched steht, die Merkliste kennt den Titel aber noch nicht.
            Series watched = await DataBuilder.PersistSeriesAsync("TKKG");
            Series unwatched = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            watched.IsWatched = true;
            unwatched.IsWatched = false;
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            WatchedTitleDataService watchedTitles = CreateWatchedTitles();
            int added = await watchedTitles.SyncFromWatchedSeriesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
            IReadOnlySet<string> titles =
                await watchedTitles.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("tkkg", titles);
            Assert.DoesNotContain("bibi blocksberg", titles);
        }

        [Fact]
        public async Task SyncFromWatchedSeries_DuplicateTitles_AddsOnlyOnce()
        {
            // Die Produktivdatenbank enthält Serien-Duplikate mit identischem Titel –
            // ohne Dedup würde der Unique-Index den ganzen Startlauf abbrechen.
            Series first = await DataBuilder.PersistSeriesAsync("TKKG");
            Series second = await DataBuilder.PersistSeriesAsync("TKKG");
            first.IsWatched = true;
            second.IsWatched = true;
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int added = await CreateWatchedTitles()
                .SyncFromWatchedSeriesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
        }
    }
}
