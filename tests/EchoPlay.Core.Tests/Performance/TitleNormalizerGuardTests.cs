using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Core.Scoring;
using System.Diagnostics;

namespace EchoPlay.Core.Tests.Performance
{
    /// <summary>
    /// Grober Wächter für den Normalisierer, der bei Suche, Import und Abgleich über jeden Titel
    /// läuft. Er schlägt erst an, wenn er um eine Größenordnung langsamer wird; feine Messungen
    /// macht das Messprojekt.
    /// </summary>
    public sealed class TitleNormalizerGuardTests
    {
        // Messlauf 20.11.2026 (BenchmarkDotNet): 1,5 ms für alle 5.118 Titel des Bestands. 100 ms
        // lassen Luft für langsame Rechner und den parallelen Testlauf und fangen trotzdem ein
        // Abrutschen um mehr als eine Größenordnung, etwa einen regulären Ausdruck, der bei jedem
        // Aufruf neu übersetzt wird.
        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);

        [Fact]
        public void Normalize_OverAllTitlesOfTheLibrary_StaysWellUnderTheLimit()
        {
            string[] titles = BenchmarkTitles.Create();

            // Der erste Durchlauf lädt und übersetzt; gemessen wird der zweite.
            _ = NormalizeAll(titles);
            Stopwatch watch = Stopwatch.StartNew();
            int length = NormalizeAll(titles);
            watch.Stop();

            Assert.True(length > 0, "Die Normalform ist leer: Die Messung liefe über nichts.");
            Assert.True(watch.Elapsed < Limit, $"Normalisieren von {titles.Length} Titeln: {watch.Elapsed.TotalMilliseconds:F1} ms, Grenze {Limit.TotalMilliseconds:F0} ms.");
        }

        private static int NormalizeAll(string[] titles) => titles.Sum(title => HoerspielTextNormalizer.Normalize(title).Length);
    }
}
