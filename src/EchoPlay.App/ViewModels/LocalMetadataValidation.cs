using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Prüft die Eingaben im Dialog „Daten bearbeiten". Liefert bei einem Fehler den Schlüssel
    /// des Hinweistexts aus den Sprachressourcen, damit der Dialog den Grund nennen kann.
    /// </summary>
    public static class LocalMetadataValidation
    {
        // Ordnernamen tragen höchstens drei Stellen; größere Nummern ließe das Einlesen fallen.
        private const int MaxEpisodeNumber = 999;

        // Tonträger mit Hörspielen gibt es seit den 1950ern, die Grenze nach oben fängt Tippfehler.
        private const int MinYear = 1900;
        private const int MaxYear = 2100;

        /// <summary>
        /// Prüft die Angaben einer Folge.
        /// </summary>
        /// <param name="title">Eingegebener Titel.</param>
        /// <param name="numberText">Eingegebene Nummer; leer bedeutet Sonderfolge.</param>
        /// <param name="yearText">Eingegebenes Jahr; leer bedeutet unbekannt.</param>
        /// <param name="numberRule">
        /// Ob die Folge eine Nummer behalten muss. Hatte sie eine, stünde die alte Nummer weiter
        /// im Ordnernamen, und das nächste Einlesen setzte sie wieder.
        /// </param>
        /// <param name="number">Die gelesene Nummer.</param>
        /// <param name="year">Das gelesene Jahr.</param>
        /// <returns>Schlüssel des Fehlertexts, oder <see langword="null"/>, wenn alles stimmt.</returns>
        public static string? ValidateEpisode(
            string? title, string? numberText, string? yearText, EpisodeNumberRule numberRule, out int? number, out int? year)
        {
            number = null;
            year = null;

            if (string.IsNullOrWhiteSpace(title))
            {
                return "EditErrorTitleEmpty";
            }

            string? numberError = ParseNumber(numberText, numberRule, out number);
            if (numberError is not null)
            {
                return numberError;
            }

            string? yearError = ParseYear(yearText, out year);
            if (yearError is not null)
            {
                // Bei einem Fehler gilt keine der beiden Angaben als gelesen.
                number = null;
            }

            return yearError;
        }

        private static string? ParseNumber(string? numberText, EpisodeNumberRule numberRule, out int? number)
        {
            number = null;
            if (string.IsNullOrWhiteSpace(numberText))
            {
                return numberRule == EpisodeNumberRule.Required ? "EditErrorNumberRequired" : null;
            }

            if (!int.TryParse(numberText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedNumber)
                || parsedNumber is < 1 or > MaxEpisodeNumber)
            {
                return "EditErrorNumber";
            }

            number = parsedNumber;
            return null;
        }

        private static string? ParseYear(string? yearText, out int? year)
        {
            year = null;
            if (string.IsNullOrWhiteSpace(yearText))
            {
                return null;
            }

            if (!int.TryParse(yearText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedYear)
                || parsedYear is < MinYear or > MaxYear)
            {
                return "EditErrorYear";
            }

            year = parsedYear;
            return null;
        }

        /// <summary>
        /// Prüft den Titel einer Serie.
        /// </summary>
        /// <param name="title">Eingegebener Titel.</param>
        /// <returns>Schlüssel des Fehlertexts, oder <see langword="null"/>, wenn er stimmt.</returns>
        public static string? ValidateSeries(string? title)
            => string.IsNullOrWhiteSpace(title) ? "EditErrorTitleEmpty" : null;
    }
}
