using EchoPlay.Core.Parsing;
using FsCheck;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Property-based-Fuzz-Tests für <see cref="EpisodeNumberParser.Extract"/>.
    /// Property: Egal welcher String als Input — der Parser darf nur null oder
    /// einen Wert in (0, 1000) zurückgeben und nie eine Exception werfen.
    /// </summary>
    public sealed class EpisodeNumberParserFuzzTests
    {
        [Property(MaxTest = 5000)]
        public bool Extract_AnyNonNullString_DoesNotThrow_AndReturnsInValidRange(NonNull<string> input)
        {
            int? result = EpisodeNumberParser.Extract(input.Get);
            return result is null || (result > 0 && result < 1000);
        }

        [Property(MaxTest = 1000)]
        public bool Extract_LongInput_TerminatesWithoutOOM(NonNull<string> input)
        {
            // 50.000 Zeichen Input darf den Parser nicht in einen Backtracking-Crash schicken.
            // Regex \d+ ist linear, sollte das aushalten.
            string longInput = string.Concat(Enumerable.Repeat(input.Get, 1000));
            int? result = EpisodeNumberParser.Extract(longInput);
            return result is null || (result > 0 && result < 1000);
        }

        [Property(MaxTest = 5000)]
        public bool Extract_OnlyDigits_ReturnsNumberOrNull(PositiveInt number)
        {
            string input = number.Get.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int? result = EpisodeNumberParser.Extract(input);

            // Der Parser akzeptiert nur 1-999. Werte ausserhalb -> null.
            if (number.Get >= 1 && number.Get < 1000)
            {
                return result == number.Get;
            }
            return result is null;
        }
    }
}
