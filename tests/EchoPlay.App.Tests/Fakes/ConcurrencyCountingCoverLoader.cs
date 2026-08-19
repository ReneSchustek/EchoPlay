using EchoPlay.LocalLibrary.Cover;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Cover-Lader, der Bilddaten liefert und dabei mitzählt, wie viele Ladevorgänge
    /// gleichzeitig laufen. Damit lässt sich die Obergrenze des Nachladens prüfen.
    /// </summary>
    /// <remarks>
    /// Die Grenze ist kein Detail: Ohne sie liefen bei einer Serie mit vierhundert Folgen
    /// vierhundert Dateizugriffe zugleich, und die Oberfläche käme zwischen den
    /// Änderungsmeldungen nicht mehr zum Zeichnen.
    /// </remarks>
    internal sealed class ConcurrencyCountingCoverLoader : ILocalCoverLoader
    {
        private static readonly byte[] Cover = [0x01, 0x02, 0x03];

        private int _running;
        private int _peak;

        /// <summary>Die höchste Zahl gleichzeitig laufender Ladevorgänge.</summary>
        public int PeakConcurrency => Volatile.Read(ref _peak);

        /// <summary>Wie oft geladen wurde.</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        private int _callCount;

        /// <inheritdoc/>
        public async Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
        {
            int current = Interlocked.Increment(ref _running);
            _ = Interlocked.Increment(ref _callCount);

            int previousPeak = Volatile.Read(ref _peak);
            while (current > previousPeak)
            {
                int exchanged = Interlocked.CompareExchange(ref _peak, current, previousPeak);
                if (exchanged == previousPeak)
                {
                    break;
                }

                previousPeak = exchanged;
            }

            // Ein Umweg über den Aufgabenplaner, damit die Ladevorgänge sich wirklich
            // überlappen können — ein sofort fertiger Aufruf liefe immer allein.
            await Task.Yield();

            _ = Interlocked.Decrement(ref _running);
            return Cover;
        }
    }
}
