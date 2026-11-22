using System.Globalization;

namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Ein Zeitpunkt der hybriden logischen Uhr: Wanduhrzeit, Zähler und Gerät.
    /// </summary>
    /// <remarks>
    /// Die Wanduhr allein reicht für den Abgleich zwischen Geräten nicht. Zwei Rechner gehen
    /// verschieden, und innerhalb einer Millisekunde entstehen mehrere Änderungen. Der Zähler ordnet
    /// Ereignisse derselben Millisekunde, die Geräte-Kennung entscheidet den Gleichstand zwischen
    /// Geräten. So sind zwei Zeitpunkte nie gleich, und jede Seite kommt zur selben Reihenfolge.
    /// <para>
    /// Die Textform ist so gebaut, dass ihr ordinaler Vergleich dieselbe Reihenfolge ergibt wie
    /// <see cref="CompareTo"/>. Damit lässt sich in der Datenbank und in Dateien sortieren, ohne die
    /// Werte zu zerlegen, und ein Programm auf einer anderen Plattform braucht nur Zeichenketten.
    /// </para>
    /// </remarks>
    /// <param name="WallMilliseconds">Millisekunden seit 1970-01-01 UTC.</param>
    /// <param name="Counter">Zähler für Ereignisse innerhalb derselben Millisekunde.</param>
    /// <param name="DeviceId">Kennung des Geräts, das den Zeitpunkt vergeben hat.</param>
    public readonly record struct HybridTimestamp(long WallMilliseconds, int Counter, Guid DeviceId)
        : IComparable<HybridTimestamp>
    {
        private const int WallDigits = 15;
        private const int CounterDigits = 9;
        private const int DeviceDigits = 32;
        private const char Separator = '.';

        /// <summary>Länge der Textform.</summary>
        public const int TextLength = WallDigits + 1 + CounterDigits + 1 + DeviceDigits;

        /// <inheritdoc/>
        public int CompareTo(HybridTimestamp other)
        {
            int byWall = WallMilliseconds.CompareTo(other.WallMilliseconds);
            if (byWall != 0)
            {
                return byWall;
            }

            int byCounter = Counter.CompareTo(other.Counter);
            return byCounter != 0
                ? byCounter
                : string.CompareOrdinal(DeviceId.ToString("N"), other.DeviceId.ToString("N"));
        }

        /// <summary>Ob dieser Zeitpunkt vor <paramref name="right"/> liegt.</summary>
        /// <param name="left">Der linke Zeitpunkt.</param>
        /// <param name="right">Der rechte Zeitpunkt.</param>
        /// <returns><see langword="true"/>, wenn <paramref name="left"/> früher ist.</returns>
        public static bool operator <(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) < 0;

        /// <summary>Ob dieser Zeitpunkt nach <paramref name="right"/> liegt.</summary>
        /// <param name="left">Der linke Zeitpunkt.</param>
        /// <param name="right">Der rechte Zeitpunkt.</param>
        /// <returns><see langword="true"/>, wenn <paramref name="left"/> später ist.</returns>
        public static bool operator >(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) > 0;

        /// <summary>Ob dieser Zeitpunkt vor oder auf <paramref name="right"/> liegt.</summary>
        /// <param name="left">Der linke Zeitpunkt.</param>
        /// <param name="right">Der rechte Zeitpunkt.</param>
        /// <returns><see langword="true"/>, wenn <paramref name="left"/> nicht später ist.</returns>
        public static bool operator <=(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) <= 0;

        /// <summary>Ob dieser Zeitpunkt nach oder auf <paramref name="right"/> liegt.</summary>
        /// <param name="left">Der linke Zeitpunkt.</param>
        /// <param name="right">Der rechte Zeitpunkt.</param>
        /// <returns><see langword="true"/>, wenn <paramref name="left"/> nicht früher ist.</returns>
        public static bool operator >=(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// Die Textform: Wanduhr, Zähler und Gerät mit fester Breite, durch Punkte getrennt.
        /// </summary>
        /// <returns>Zum Beispiel <c>001763640000000.000000002.3f2a…</c>.</returns>
        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{WallMilliseconds:D15}{Separator}{Counter:D9}{Separator}{DeviceId:N}");

        /// <summary>
        /// Liest die Textform aus <see cref="ToString"/>. Alles andere wird abgelehnt, auch
        /// negative Werte und fehlende Stellen, denn ein fremder Wert soll nicht halb verstanden werden.
        /// </summary>
        /// <param name="text">Die Textform.</param>
        /// <param name="timestamp">Der gelesene Zeitpunkt.</param>
        /// <returns><see langword="true"/>, wenn der Text gültig war.</returns>
        public static bool TryParse(string? text, out HybridTimestamp timestamp)
        {
            timestamp = default;
            if (text is null || text.Length != TextLength
                || text[WallDigits] != Separator
                || text[WallDigits + 1 + CounterDigits] != Separator)
            {
                return false;
            }

            ReadOnlySpan<char> wall = text.AsSpan(0, WallDigits);
            ReadOnlySpan<char> counter = text.AsSpan(WallDigits + 1, CounterDigits);
            ReadOnlySpan<char> device = text.AsSpan(WallDigits + 1 + CounterDigits + 1, DeviceDigits);

            if (!AllDigits(wall) || !AllDigits(counter)
                || !long.TryParse(wall, NumberStyles.None, CultureInfo.InvariantCulture, out long wallValue)
                || !int.TryParse(counter, NumberStyles.None, CultureInfo.InvariantCulture, out int counterValue)
                || !Guid.TryParseExact(device, "N", out Guid deviceValue))
            {
                return false;
            }

            timestamp = new HybridTimestamp(wallValue, counterValue, deviceValue);
            return true;
        }

        private static bool AllDigits(ReadOnlySpan<char> digits)
        {
            foreach (char c in digits)
            {
                if (c is < '0' or > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
