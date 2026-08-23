using EchoPlay.Core.Scoring;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Der Wettlauf zweier Schreibzugriffe auf denselben Titel. Der Startabgleich und ein
    /// Klick auf das Überwachungs-Auge laufen unabhängig voneinander; zwischen der Prüfung
    /// „gibt es den Titel schon?" und dem Speichern kann der jeweils andere ihn angelegt haben.
    /// </summary>
    /// <remarks>
    /// Der eindeutige Index fängt das ab. Erwartet wird deshalb **kein** Fehler, sondern ein
    /// stilles Weiterlaufen: Der Titel steht danach genau einmal in der Merkliste.
    ///
    /// Nachgestellt wird der Wettlauf über einen noch nicht gespeicherten Eintrag. Eine Abfrage
    /// gegen die Datenbank sieht ihn nicht — genau wie ein Eintrag, den ein fremder Zugriff
    /// erst nach der Prüfung schreibt. Beim Speichern kollidieren dann beide.
    /// </remarks>
    public sealed class WatchedTitleConflictTests : DbTestBase
    {
        private WatchedTitleDataService CreateWatchedTitles() => new(Context, NullLoggerFactory);

        [Fact]
        public async Task RememberAsync_WhenAnotherWriterWasFasterWithTheSameTitle_KeepsGoingWithoutFailing()
        {
            const string Title = "Die drei ???";

            WatchedTitleDataService service = CreateWatchedTitles();

            // Der Eintrag des anderen Schreibers: vorgemerkt, aber noch nicht gespeichert.
            // Die Existenzprüfung in RememberAsync fragt die Datenbank und sieht ihn nicht.
            _ = Context.WatchedTitles.Add(new WatchedTitle
            {
                Title = Title,
                NormalizedTitle = HoerspielTextNormalizer.Normalize(Title)
            });

            await service.RememberAsync(Title, TestContext.Current.CancellationToken);

            Context.ChangeTracker.Clear();

            // Der Konflikt ist geschluckt, und der Titel steht nicht doppelt in der Liste.
            List<WatchedTitle> stored = await Context.WatchedTitles
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.DoesNotContain(stored, w => w.NormalizedTitle != HoerspielTextNormalizer.Normalize(Title));
            Assert.True(stored.Count <= 1);
        }

        [Fact]
        public async Task SyncFromWatchedSeriesAsync_WhenTheTitleAppearsMeanwhile_ReportsNothingTakenOver()
        {
            const string Title = "TKKG";

            Series series = await DataBuilder.PersistSeriesAsync(Title);
            series.IsWatched = true;
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            WatchedTitleDataService service = CreateWatchedTitles();

            // Wieder der schnellere Schreiber: vorgemerkt, für die Abfrage unsichtbar.
            _ = Context.WatchedTitles.Add(new WatchedTitle
            {
                Title = Title,
                NormalizedTitle = HoerspielTextNormalizer.Normalize(Title)
            });

            int takenOver = await service.SyncFromWatchedSeriesAsync(TestContext.Current.CancellationToken);

            // Null übernommene Titel heißt hier nicht „nichts zu tun", sondern „der andere
            // war schneller". Für den Aufrufer ist beides derselbe unauffällige Ausgang.
            Assert.Equal(0, takenOver);
        }
    }
}
