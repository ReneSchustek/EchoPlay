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
        [Fact]
        public async Task SetWatched_Enabling_RemembersNormalizedTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Die drei ???");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyCollection<string> titles =
                await service.GetWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("die drei ", titles);
        }

        [Fact]
        public async Task SetWatched_Disabling_ForgetsTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);
            await service.SetWatchedAsync(series.Id, false, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyCollection<string> titles =
                await service.GetWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(titles);
        }

        [Fact]
        public async Task SetFavorite_AlsoRemembersTitle()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Fünf Freunde");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetFavoriteAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyCollection<string> titles =
                await service.GetWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Umlaut wird auf die ASCII-Vergleichsform abgebildet.
            Assert.Contains("fuenf freunde", titles);
        }

        [Fact]
        public async Task SetWatched_Twice_KeepsSingleEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);
            await service.SetWatchedAsync(series.Id, true, cancellationToken: TestContext.Current.CancellationToken);

            // Der Unique-Index würde beim zweiten Insert werfen – die Prüfung muss vorher greifen.
            Assert.Equal(1, await Context.WatchedTitles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SyncWatchedTitles_AddsMissingEntriesForWatchedSeries()
        {
            // Altbestand: IsWatched steht, die Merkliste kennt den Titel aber noch nicht.
            Series watched = await DataBuilder.PersistSeriesAsync("TKKG");
            Series unwatched = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            watched.IsWatched = true;
            unwatched.IsWatched = false;
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            int added = await service.SyncWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
            IReadOnlyCollection<string> titles =
                await service.GetWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("tkkg", titles);
            Assert.DoesNotContain("bibi blocksberg", titles);
        }

        [Fact]
        public async Task SyncWatchedTitles_DuplicateTitles_AddsOnlyOnce()
        {
            // Die Produktivdatenbank enthält Serien-Duplikate mit identischem Titel –
            // ohne Dedup würde der Unique-Index den ganzen Startlauf abbrechen.
            Series first = await DataBuilder.PersistSeriesAsync("TKKG");
            Series second = await DataBuilder.PersistSeriesAsync("TKKG");
            first.IsWatched = true;
            second.IsWatched = true;
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            int added = await service.SyncWatchedTitlesAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
        }
    }
}
