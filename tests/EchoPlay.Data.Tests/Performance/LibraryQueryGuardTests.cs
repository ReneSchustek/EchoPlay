using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using System.Diagnostics;

namespace EchoPlay.Data.Tests.Performance
{
    /// <summary>
    /// Grober Wächter für die teuerste Abfrage der Startseite: alle Folgen der abonnierten Serien
    /// über den Messbestand in Produktivgröße. Er schlägt erst an, wenn die Abfrage um eine
    /// Größenordnung langsamer wird; feine Messungen macht das Messprojekt.
    /// </summary>
    public sealed class LibraryQueryGuardTests : DbTestBase
    {
        // Messlauf 20.11.2026 (BenchmarkDotNet, Datei auf SSD): 16 ms im Mittel. Der Wächter läuft
        // im Arbeitsspeicher, aber neben dem übrigen Testlauf und auf beliebigen Rechnern; 250 ms
        // lassen dafür das Fünfzehnfache Luft und fangen trotzdem ein Abrutschen um eine
        // Größenordnung, etwa eine Abfrage je Folge statt einer für alle.
        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

        [Fact]
        public async Task EpisodesOfSubscribedSeries_StayWellUnderTheLimit()
        {
            IReadOnlyList<Guid> subscribed = LibrarySeed.Fill(Context);
            EpisodeDataService sut = new(Context, NullLoggerFactory);

            // Der erste Aufruf übersetzt die Abfrage; gemessen wird der zweite, wie in der App.
            _ = await sut.GetBySeriesIdsAsync(subscribed, TestContext.Current.CancellationToken);
            Stopwatch watch = Stopwatch.StartNew();
            IReadOnlyList<Episode> episodes = await sut.GetBySeriesIdsAsync(subscribed, TestContext.Current.CancellationToken);
            watch.Stop();

            Assert.True(episodes.Count > LibraryShape.EpisodeCount / 2, $"Nur {episodes.Count} Folgen: Die Messung liefe über den falschen Bestand.");
            Assert.True(watch.Elapsed < Limit, $"Folgen der abonnierten Serien: {watch.Elapsed.TotalMilliseconds:F0} ms, Grenze {Limit.TotalMilliseconds:F0} ms.");
        }
    }
}
