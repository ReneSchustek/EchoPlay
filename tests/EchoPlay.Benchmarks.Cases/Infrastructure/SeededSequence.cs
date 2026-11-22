namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Eine vorhersagbare Zahlenfolge aus einem festen Startwert (SplitMix64) für den Messbestand.
    /// </summary>
    /// <remarks>
    /// Kein <see cref="Random"/>: Dessen Folge zu einem Startwert ist nicht über alle .NET-Fassungen
    /// zugesagt, und ein anderer Bestand bräche die Messreihe. Für Sicherheit taugt die Folge nicht
    /// und soll es nicht; sie muss nur auf jedem Rechner dieselbe sein.
    /// </remarks>
    /// <param name="seed">Der Startwert.</param>
    internal sealed class SeededSequence(ulong seed)
    {
        private ulong _state = seed;

        /// <summary>Eine Zahl von 0 bis ausschließlich <paramref name="maxExclusive"/>.</summary>
        /// <param name="maxExclusive">Obergrenze, größer als 0.</param>
        /// <returns>Die nächste Zahl der Folge im Bereich.</returns>
        public int Next(int maxExclusive)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
            return (int)(NextUInt64() % (ulong)maxExclusive);
        }

        /// <summary>Eine Zahl von 0 bis ausschließlich 1.</summary>
        /// <returns>Die nächste Zahl der Folge im Bereich.</returns>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        private ulong NextUInt64()
        {
            // SplitMix64, die Konstanten sind die des veröffentlichten Verfahrens.
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
