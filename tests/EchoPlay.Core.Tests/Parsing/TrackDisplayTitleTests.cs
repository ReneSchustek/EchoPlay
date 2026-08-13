using EchoPlay.Core.Parsing;

namespace EchoPlay.Core.Tests.Parsing
{
    /// <summary>
    /// Prüft, wie der Anzeigename einer Spur zustande kommt: Titel aus der Kennzeichnung
    /// bevorzugt, Dateiname als Rückfallebene — aufgeräumt, aber nie leer.
    /// </summary>
    public sealed class TrackDisplayTitleTests
    {
        [Fact]
        public void Choose_TagTitleIsSet_PrefersTagTitle()
        {
            string result = TrackDisplayTitle.Choose(
                "Das leere Haus (Teil 1)",
                @"C:\Audio\Serie\01 - Das leere Haus (Teil 1).mp3",
                trackNumber: 1);

            Assert.Equal("Das leere Haus (Teil 1)", result);
        }

        [Fact]
        public void Choose_TagTitleHasSurroundingWhitespace_IsTrimmed()
        {
            string result = TrackDisplayTitle.Choose("  Das leere Haus  ", @"C:\Audio\01.mp3", trackNumber: 1);

            Assert.Equal("Das leere Haus", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Choose_TagTitleIsMissing_FallsBackToFileName(string? tagTitle)
        {
            string result = TrackDisplayTitle.Choose(
                tagTitle,
                @"C:\Audio\Serie\02 - Das leere Haus (Teil 2).mp3",
                trackNumber: 2);

            Assert.Equal("Das leere Haus (Teil 2)", result);
        }

        [Theory]
        [InlineData("Track 01")]
        [InlineData("track")]
        [InlineData("Titel 3")]
        [InlineData("Audio Track 12")]
        [InlineData("Unbekannt")]
        public void Choose_TagTitleIsOnlyAPlaceholder_FallsBackToFileName(string tagTitle)
        {
            // Wo der Ripper nur durchnummeriert hat, ist der Dateiname die bessere Auskunft.
            string result = TrackDisplayTitle.Choose(
                tagTitle,
                @"C:\Audio\Serie\03 - Der Fluch der Mumie.mp3",
                trackNumber: 3);

            Assert.Equal("Der Fluch der Mumie", result);
        }

        [Fact]
        public void Choose_TagTitleContainsPlaceholderWord_IsStillUsed()
        {
            // Nur der reine Platzhalter zählt – "Der Track ins Nichts" ist ein echter Titel.
            string result = TrackDisplayTitle.Choose(
                "Der Track ins Nichts",
                @"C:\Audio\Serie\04 - Irgendwas.mp3",
                trackNumber: 4);

            Assert.Equal("Der Track ins Nichts", result);
        }

        [Fact]
        public void FromFilePath_AlwaysRemovesTheExtension()
        {
            string result = TrackDisplayTitle.FromFilePath(@"C:\Audio\Serie\Das leere Haus.mp3", trackNumber: 1);

            Assert.Equal("Das leere Haus", result);
        }

        [Theory]
        [InlineData(@"C:\Audio\01 - Das leere Haus.mp3")]
        [InlineData(@"C:\Audio\01. Das leere Haus.mp3")]
        [InlineData(@"C:\Audio\01_Das leere Haus.mp3")]
        [InlineData(@"C:\Audio\1 Das leere Haus.mp3")]
        public void FromFilePath_LeadingNumberIsTheTrackNumber_IsRemoved(string filePath)
        {
            // Die Nummer steht bereits in eigener Spalte – zweimal wäre sie zu viel.
            string result = TrackDisplayTitle.FromFilePath(filePath, trackNumber: 1);

            Assert.Equal("Das leere Haus", result);
        }

        [Fact]
        public void FromFilePath_LeadingNumberIsNotTheTrackNumber_IsKept()
        {
            // "116 Klassenfahrt" ist ein Titel, keine laufende Nummer.
            string result = TrackDisplayTitle.FromFilePath(@"C:\Audio\116 Klassenfahrt.mp3", trackNumber: 1);

            Assert.Equal("116 Klassenfahrt", result);
        }

        [Fact]
        public void FromFilePath_NumberIsNotFollowedBySeparator_IsKept()
        {
            // Kassetten-Rips wie "01a Spuk" tragen eine Seitenkennung – da wird nichts entfernt.
            string result = TrackDisplayTitle.FromFilePath(@"C:\Audio\01a Spuk in der Werkstatt.mp3", trackNumber: 1);

            Assert.Equal("01a Spuk in der Werkstatt", result);
        }

        [Fact]
        public void FromFilePath_FileIsNamedOnlyAfterItsNumber_KeepsTheName()
        {
            // Sonst bliebe die Zeile leer.
            string result = TrackDisplayTitle.FromFilePath(@"C:\Audio\05.mp3", trackNumber: 5);

            Assert.Equal("05", result);
        }

        [Fact]
        public void FromFilePath_NameStartsLowercase_IsCapitalized()
        {
            string result = TrackDisplayTitle.FromFilePath(@"C:\Audio\01 - spuk in der werkstatt.mp3", trackNumber: 1);

            Assert.Equal("Spuk in der werkstatt", result);
        }

        [Fact]
        public void FromFilePath_PathIsEmpty_ReturnsEmpty()
        {
            string result = TrackDisplayTitle.FromFilePath(string.Empty, trackNumber: 1);

            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData("Track 7", false)]
        [InlineData("Das leere Haus", true)]
        public void IsMeaningful_ReportsWhetherTheTagTitleIsWorthShowing(string? tagTitle, bool expected)
        {
            Assert.Equal(expected, TrackDisplayTitle.IsMeaningful(tagTitle));
        }
    }
}
