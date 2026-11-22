using EchoPlay.App.ViewModels;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Eingaben des Dialogs „Daten bearbeiten": Jeder Fehler nennt einen Grund, gültige
    /// Eingaben kommen als Zahlen heraus.
    /// </summary>
    public sealed class LocalMetadataValidationTests
    {
        [Fact]
        public void ValidateEpisode_ValidInput_ReturnsNumbers()
        {
            string? problem = LocalMetadataValidation.ValidateEpisode("Tanz der Herzen", " 66 ", "2019", numberRequired: true, out int? number, out int? year);

            Assert.Null(problem);
            Assert.Equal(66, number);
            Assert.Equal(2019, year);
        }

        [Theory]
        [InlineData("", "1", "", "EditErrorTitleEmpty")]
        [InlineData("   ", "1", "", "EditErrorTitleEmpty")]
        [InlineData("Titel", "0", "", "EditErrorNumber")]
        [InlineData("Titel", "1000", "", "EditErrorNumber")]
        [InlineData("Titel", "-3", "", "EditErrorNumber")]
        [InlineData("Titel", "zwölf", "", "EditErrorNumber")]
        [InlineData("Titel", "1", "1899", "EditErrorYear")]
        [InlineData("Titel", "1", "2101", "EditErrorYear")]
        [InlineData("Titel", "1", "neunzehn", "EditErrorYear")]
        public void ValidateEpisode_InvalidInput_NamesTheReason(string title, string numberText, string yearText, string expected)
        {
            Assert.Equal(expected, LocalMetadataValidation.ValidateEpisode(title, numberText, yearText, numberRequired: false, out _, out _));
        }

        [Fact]
        public void ValidateEpisode_EmptyNumberOnNumberedEpisode_IsRejected()
        {
            // Die alte Nummer stünde weiter im Ordnernamen und käme mit dem nächsten Einlesen zurück.
            Assert.Equal("EditErrorNumberRequired",
                LocalMetadataValidation.ValidateEpisode("Titel", "", "", numberRequired: true, out _, out _));
        }

        [Fact]
        public void ValidateEpisode_EmptyNumberAndYearOnSpecial_IsAccepted()
        {
            string? problem = LocalMetadataValidation.ValidateEpisode("Adventskalender", "", "", numberRequired: false, out int? number, out int? year);

            Assert.Null(problem);
            Assert.Null(number);
            Assert.Null(year);
        }

        [Theory]
        [InlineData("", "EditErrorTitleEmpty")]
        [InlineData("Die drei ???", null)]
        public void ValidateSeries_ChecksTheTitle(string title, string? expected)
            => Assert.Equal(expected, LocalMetadataValidation.ValidateSeries(title));
    }
}
