using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="DashboardPositionDataService"/>.
    /// Prüft Speicherung, Ersetzung und bereichsbezogene Filterung
    /// von benutzerdefinierten Dashboard-Positionen.
    /// </summary>
    public class DashboardPositionTests : DbTestBase
    {
        /// <summary>
        /// Legt eine Serie in der DB an und gibt ihre Id zurück.
        /// DashboardPositions referenzieren Serien über FK –
        /// ohne existierende Serie schlägt SaveChanges fehl.
        /// </summary>
        private async Task<Guid> CreateSeriesAsync(string title = "Test-Serie")
        {
            Series series = new() { Title = title };
            Context.Series.Add(series);
            await Context.SaveChangesAsync();
            return series.Id;
        }
        [Fact]
        public async Task GetBySectionAsync_ReturnsEmptyList_WhenNoPositionsExist()
        {
            // Leere Datenbank – es dürfen keine Positionen zurückkommen
            DashboardPositionDataService service = new(Context, NullLoggerFactory);

            IReadOnlyList<DashboardPosition> result = await service.GetBySectionAsync("Neuerscheinungen");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SaveOrderAsync_CreatesPositionsWithCorrectValues()
        {
            // Drei Serien in definierter Reihenfolge speichern und Werte prüfen
            Guid seriesA = await CreateSeriesAsync("Serie A");
            Guid seriesB = await CreateSeriesAsync("Serie B");
            Guid seriesC = await CreateSeriesAsync("Serie C");
            List<Guid> seriesIds = [seriesA, seriesB, seriesC];

            DashboardPositionDataService service = new(Context, NullLoggerFactory);
            await service.SaveOrderAsync("Favoriten", seriesIds);

            // ChangeTracker leeren, damit der nächste Lesevorgang tatsächlich aus der DB kommt
            Context.ChangeTracker.Clear();

            IReadOnlyList<DashboardPosition> result = await service.GetBySectionAsync("Favoriten");

            Assert.Equal(3, result.Count);
            Assert.Equal(seriesA, result[0].SeriesId);
            Assert.Equal("Favoriten", result[0].Section);
            Assert.Equal(0, result[0].Position);
            Assert.Equal(seriesB, result[1].SeriesId);
            Assert.Equal(1, result[1].Position);
            Assert.Equal(seriesC, result[2].SeriesId);
            Assert.Equal(2, result[2].Position);
        }

        [Fact]
        public async Task GetBySectionAsync_ReturnsPositionsOrderedByPosition()
        {
            // Positionen absichtlich in umgekehrter Reihenfolge anlegen,
            // um sicherzustellen, dass GetBySectionAsync nach Position sortiert
            Guid idHigh = await CreateSeriesAsync("Serie High");
            Guid idLow = await CreateSeriesAsync("Serie Low");
            Guid idMid = await CreateSeriesAsync("Serie Mid");

            DashboardPosition positionHigh = new()
            {
                SeriesId = idHigh,
                Section = "Neuerscheinungen",
                Position = 2
            };
            DashboardPosition positionLow = new()
            {
                SeriesId = idLow,
                Section = "Neuerscheinungen",
                Position = 0
            };
            DashboardPosition positionMid = new()
            {
                SeriesId = idMid,
                Section = "Neuerscheinungen",
                Position = 1
            };

            // Bewusst in unsortierter Reihenfolge einfügen
            Context.DashboardPositions.AddRange(positionHigh, positionLow, positionMid);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            DashboardPositionDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<DashboardPosition> result = await service.GetBySectionAsync("Neuerscheinungen");

            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0].Position);
            Assert.Equal(1, result[1].Position);
            Assert.Equal(2, result[2].Position);
        }

        [Fact]
        public async Task SaveOrderAsync_ReplacesExistingPositions()
        {
            // Zweimaliges Speichern: die zweite Reihenfolge muss die erste vollständig ersetzen
            Guid seriesA = await CreateSeriesAsync("Serie A");
            Guid seriesB = await CreateSeriesAsync("Serie B");

            DashboardPositionDataService service = new(Context, NullLoggerFactory);

            // Erste Reihenfolge: A vor B
            await service.SaveOrderAsync("Favoriten", [seriesA, seriesB]);
            Context.ChangeTracker.Clear();

            // Zweite Reihenfolge: B vor A – muss die erste komplett ersetzen
            await service.SaveOrderAsync("Favoriten", [seriesB, seriesA]);
            Context.ChangeTracker.Clear();

            IReadOnlyList<DashboardPosition> result = await service.GetBySectionAsync("Favoriten");

            Assert.Equal(2, result.Count);
            Assert.Equal(seriesB, result[0].SeriesId);
            Assert.Equal(0, result[0].Position);
            Assert.Equal(seriesA, result[1].SeriesId);
            Assert.Equal(1, result[1].Position);
        }

        [Fact]
        public async Task GetBySectionAsync_ReturnsOnlyRequestedSection()
        {
            // Positionen in zwei verschiedenen Bereichen anlegen –
            // Abfrage für einen Bereich darf nur dessen Einträge liefern
            Guid seriesFav = await CreateSeriesAsync("Favorit");
            Guid seriesNeu = await CreateSeriesAsync("Neuerscheinung");

            DashboardPositionDataService service = new(Context, NullLoggerFactory);

            await service.SaveOrderAsync("Favoriten", [seriesFav]);
            await service.SaveOrderAsync("Neuerscheinungen", [seriesNeu]);
            Context.ChangeTracker.Clear();

            IReadOnlyList<DashboardPosition> favoritenResult = await service.GetBySectionAsync("Favoriten");
            IReadOnlyList<DashboardPosition> neuResult = await service.GetBySectionAsync("Neuerscheinungen");

            Assert.Single(favoritenResult);
            Assert.Equal(seriesFav, favoritenResult[0].SeriesId);

            Assert.Single(neuResult);
            Assert.Equal(seriesNeu, neuResult[0].SeriesId);
        }
    }
}
