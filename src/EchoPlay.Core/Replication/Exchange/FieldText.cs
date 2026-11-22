using System.Globalization;

namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>
    /// Wandelt Feldwerte in ihre Textform im Protokoll und zurück. Die Formen sind fest und
    /// sprachunabhängig, damit jedes Gerät und jede Plattform sie gleich liest.
    /// </summary>
    /// <remarks>
    /// Wahrheitswerte als <c>true</c>/<c>false</c>, Zahlen ohne Gruppierung mit Punkt, Zeitspannen
    /// als <c>[-][d.]hh:mm:ss[.fffffff]</c>, Zeitpunkte nach ISO 8601 (Format <c>O</c>),
    /// Aufzählungen mit ihrem Namen. Ein leerer Wert ist <see langword="null"/>, nicht ein leerer Text.
    /// </remarks>
    public static class FieldText
    {
        /// <summary>Schreibt einen Wert in seine Textform.</summary>
        /// <param name="value">Der Wert.</param>
        /// <returns>Die Textform, oder <see langword="null"/> für einen leeren Wert.</returns>
        /// <exception cref="NotSupportedException">Der Typ hat keine festgelegte Textform.</exception>
        public static string? Format(object? value) => value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
            Guid id => id.ToString("N"),
            Enum choice => choice.ToString(),
            _ => throw new NotSupportedException($"Für {value.GetType().Name} ist keine Textform festgelegt."),
        };

        /// <summary>Liest einen Wert aus seiner Textform.</summary>
        /// <param name="text">Die Textform, oder <see langword="null"/>.</param>
        /// <param name="type">Der Zieltyp, auch ein Nullable.</param>
        /// <param name="value">Der gelesene Wert.</param>
        /// <returns><see langword="false"/>, wenn der Text nicht zum Typ passt.</returns>
        public static bool TryParse(string? text, Type type, out object? value)
        {
            ArgumentNullException.ThrowIfNull(type);
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            value = null;
            if (text is null)
            {
                return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
            }

            bool parsed;
            (parsed, value) = target switch
            {
                _ when target == typeof(string) => (true, text),
                _ when target == typeof(bool) => text switch { "true" => (true, true), "false" => (true, false), _ => (false, null) },
                _ when target == typeof(int) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i) ? (true, i) : (false, null),
                _ when target == typeof(long) => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l) ? (true, l) : (false, null),
                _ when target == typeof(double) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (true, d) : (false, null),
                _ when target == typeof(TimeSpan) => TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan t) ? (true, t) : (false, null),
                _ when target == typeof(DateTime) => DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt) ? (true, dt) : (false, null),
                _ when target == typeof(Guid) => Guid.TryParseExact(text, "N", out Guid g) ? (true, g) : (false, null),
                _ when target.IsEnum => Enum.TryParse(target, text, ignoreCase: false, out object? e) && Enum.IsDefined(target, e!) ? (true, e) : (false, null),
                _ => (false, null),
            };
            return parsed;
        }
    }
}
