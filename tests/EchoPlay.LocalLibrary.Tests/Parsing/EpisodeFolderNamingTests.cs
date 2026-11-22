using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Tests.Parsing
{
    /// <summary>
    /// Tests für <see cref="EpisodeFolderNaming"/>: Titel aus dem Album-Tag, Name nach dem Muster.
    /// </summary>
    public sealed class EpisodeFolderNamingTests
    {
        [Theory]
        [InlineData("Folge 87: Geheimnisvoller Liebestrank", 87, "Geheimnisvoller Liebestrank")]
        [InlineData("Die drei !!! 87: Geheimnisvoller Liebestrank", 87, "Geheimnisvoller Liebestrank")]
        [InlineData("087 - Geheimnisvoller Liebestrank", 87, "Geheimnisvoller Liebestrank")]
        [InlineData("Geheimnisvoller Liebestrank", 87, "Geheimnisvoller Liebestrank")]
        [InlineData("Die drei Ausrufezeichen - Geheimnisvoller Liebestrank", 87, "Geheimnisvoller Liebestrank")]
        public void CleanAlbumTitle_StripsNumberAndSeriesPrefix(string album, int number, string expected)
        {
            Assert.Equal(expected, EpisodeFolderNaming.CleanAlbumTitle(album, number, "Die drei Ausrufezeichen"));
        }

        [Fact]
        public void CleanAlbumTitle_NumberDiffersFromEpisode_KeepsIt()
        {
            // „Teil 2: …" in Folge 14 ist kein Vorspann, sondern Teil des Titels.
            Assert.Equal("Teil 2: Die Rückkehr", EpisodeFolderNaming.CleanAlbumTitle("Teil 2: Die Rückkehr", 14, "TKKG"));
        }

        [Fact]
        public void CleanAlbumTitle_OnlyPrefix_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, EpisodeFolderNaming.CleanAlbumTitle("Folge 87:", 87, "Die drei Ausrufezeichen"));
        }

        [Theory]
        [InlineData("die Handy-Falle", "Die Handy-Falle", true)]
        [InlineData("Vorsicht Strandhaie", "Vorsicht, Strandhaie!", true)]
        [InlineData("Gefährlicher Chat", "Gefaehrlicher Chat", false)]
        [InlineData("Geheimnisvoller Liebestrank", "Liebestrank", false)]
        public void SameTitle_OnlyCaseAndPunctuationDiffer_IsSame(string first, string second, bool expected)
        {
            Assert.Equal(expected, EpisodeFolderNaming.SameTitle(first, second));
        }

        [Theory]
        [InlineData("{number:000} - {title}", 7, "Skaterfieber", "007 - Skaterfieber")]
        [InlineData("{number} - {title}", 7, "Skaterfieber", "7 - Skaterfieber")]
        [InlineData("Folge {number:00} {title}", 7, "Skaterfieber", "Folge 07 Skaterfieber")]
        [InlineData("{number:000} - {title}", 1234, "Zu groß", "1234 - Zu groß")]
        public void Render_FillsPattern(string pattern, int number, string title, string expected)
        {
            Assert.Equal(expected, EpisodeFolderNaming.Render(pattern, number, title));
        }

        [Fact]
        public void Render_UnknownPlaceholder_ReturnsNull()
        {
            // {*} beschreibt, was im Namen stehen darf, nicht, was hineingehört.
            Assert.Null(EpisodeFolderNaming.Render("{*} - {number:000} - {title}", 7, "Skaterfieber"));
        }

        [Theory]
        [InlineData("Mission: Gipfelglück", "Mission - Gipfelglück")]
        [InlineData("Wer ist Mr. X?", "Wer ist Mr. X")]
        [InlineData("AC/DC", "AC-DC")]
        [InlineData("Ende...", "Ende")]
        [InlineData("Zu   viele  Leerzeichen ", "Zu viele Leerzeichen")]
        [InlineData("\"Zitat\" <mit> | Sternchen*", "Zitat mit Sternchen")]
        public void Sanitize_ProducesAValidFolderName(string input, string expected)
        {
            Assert.Equal(expected, EpisodeFolderNaming.Sanitize(input));
        }
    }
}
