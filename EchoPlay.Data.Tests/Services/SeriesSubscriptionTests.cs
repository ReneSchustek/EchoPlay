using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für den Abonnement-Mechanismus der <see cref="SeriesDataService"/>-Klasse.
    /// Prüft Filterung, Persistenz und Sortierung rund um das <c>IsSubscribed</c>-Flag.
    /// </summary>
    public class SeriesSubscriptionTests : DbTestBase
    {
        [Fact]
        public async Task GetSubscribed_ReturnsOnlySubscribed()
        {
            // Nicht abonnierte Serie darf nicht im Ergebnis erscheinen
            await DataBuilder.PersistSeriesAsync("TKKG");

            Series subscribed = await DataBuilder.PersistSeriesAsync("Die drei ???");
            subscribed.IsSubscribed = true;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetSubscribedAsync();

            Assert.Single(result);
            Assert.Equal("Die drei ???", result[0].Title);
        }

        [Fact]
        public async Task SetSubscribed_PersistsFlag()
        {
            // Nach SetSubscribedAsync(true) muss IsSubscribed in der DB true sein
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            await service.SetSubscribedAsync(series.Id, true);

            // ChangeTracker leeren, damit der nächste FindAsync wirklich aus der DB liest
            Context.ChangeTracker.Clear();
            Series? updated = await Context.Series.FindAsync(series.Id);

            Assert.True(updated!.IsSubscribed);
        }

        [Fact]
        public async Task GetSubscribed_ExcludesSoftDeleted()
        {
            // Gelöschte Serien dürfen nicht zurückgegeben werden, auch wenn abonniert
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            series.IsSubscribed = true;
            series.MarkAsDeleted(DateTime.UtcNow);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetSubscribedAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSubscribed_SortsAlphabetically()
        {
            // Drei abonnierte Serien werden unabhängig von der Einfügereihenfolge alphabetisch sortiert
            Series bibi = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            Series tkkg = await DataBuilder.PersistSeriesAsync("TKKG");
            Series drei = await DataBuilder.PersistSeriesAsync("Die drei ???");

            bibi.IsSubscribed = true;
            tkkg.IsSubscribed = true;
            drei.IsSubscribed = true;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            SeriesDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Series> result = await service.GetSubscribedAsync();

            Assert.Equal(3, result.Count);
            Assert.Equal("Bibi Blocksberg", result[0].Title);
            Assert.Equal("Die drei ???", result[1].Title);
            Assert.Equal("TKKG", result[2].Title);
        }
    }
}
