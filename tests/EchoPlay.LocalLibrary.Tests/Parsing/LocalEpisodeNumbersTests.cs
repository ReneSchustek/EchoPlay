using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Tests.Parsing
{
    /// <summary>
    /// Tests für <see cref="LocalEpisodeNumbers"/>.
    /// Die Testdaten stammen aus einer echten Sammlung mit 92 Serienordnern — jeder Fall hier
    /// hat dort zu einer falschen Meldung geführt (2978 gemeldete Lücken vor der Korrektur, 78 danach).
    /// </summary>
    public sealed class LocalEpisodeNumbersTests
    {
        [Fact]
        public void Scan_YearInFolderName_IsNotAnEpisodeNumber()
        {
            // „Stenkelfeld" nummeriert nach Jahrgang. Als Folge 2008 gelesen entstanden
            // 1999 erfundene Lücken.
            string[] folders =
            [
                "2006 - Die Jubiläumsfeier",
                "2007 - Der Weihnachtsmarkt",
                "2008 - Das Sommerfest"
            ];

            Assert.Empty(LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_BareNumberFolders_AreRecognised()
        {
            // Ordner ohne Titelteil: alle bisherigen Muster verlangten einen Titel,
            // deshalb galten 38 von 67 vorhandenen Folgen als fehlend.
            string[] folders = ["001", "002", "003"];

            Assert.Equal([1, 2, 3], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_MissingSpaceAfterNumber_IsRecognised()
        {
            // Echter Tippfehler in der Sammlung: „203- …" statt „203 - …".
            // Die Folge liegt vor, wurde aber als fehlend gemeldet.
            string[] folders =
            [
                "202 - Ein Paradies für Diebe",
                "203- Der Räuber mit der Weihnachtsmaske",
                "204 - Verschwörung auf hoher See"
            ];

            Assert.Equal([202, 203, 204], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_MixedNamingWithinOneSeries_FindsAllNumbers()
        {
            // Vorher gewann ein einziges Muster für die ganze Serie; abweichend
            // benannte Ordner zählten als fehlend.
            string[] folders =
            [
                "001 - Der erste Fall",
                "Folge 002 - Der zweite Fall",
                "TKKG - 003 - Der dritte Fall",
                "004"
            ];

            Assert.Equal([1, 2, 3, 4], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_SingleNumberAmongManyUnnumbered_IsIgnored()
        {
            // „Arsène Lupin – 813" ist ein Romantitel. Als Folge 813 gelesen
            // entstanden 812 erfundene Lücken.
            string[] folders =
            [
                "Arsène Lupin - Der Gentleman-Einbrecher",
                "Arsène Lupin und die Insel der 30 Särge",
                "Arsène Lupin - 813",
                "Arsène Lupin gegen Herlock Sholmes"
            ];

            Assert.Empty(LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_SingleFolderWithNumber_KeepsTheNumber()
        {
            // Gegenprobe: Bei einer einzelnen Folge ist die Zahl sehr wohl die Folgennummer.
            string[] folders = ["001 - Die Hexe von Rungholt"];

            Assert.Equal([1], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_LoneNumberFittingCollectionSize_IsKept()
        {
            // Gegenprobe zur Lupin-Regel: Folge 5 neben Sonderfolge und Bonusordner ist
            // plausibel — verworfen wird nur, was nicht zur Sammlungsgröße passt.
            string[] folders = ["000 - Sonderfolge", "005 - Fünfte Folge", "Bonus - Extra"];

            Assert.Equal([5], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void Scan_SpecialsNumberedZero_AreNotCounted()
        {
            // Adventskalender und Sonderfolgen laufen unter „000" und sind keine regulären Folgen.
            string[] folders =
            [
                "000 - Adventskalender 2021",
                "001 - Die Jagd nach den Millionendieben",
                "002 - Der Schlangenmensch"
            ];

            Assert.Equal([1, 2], LocalEpisodeNumbers.Scan(folders));
        }

        [Fact]
        public void FindGaps_StartsAtLowestPresentNumber_NotAtOne()
        {
            // Wer erst ab Folge 50 sammelt, hat keine 49 Lücken.
            IReadOnlyList<int> numbers = [50, 51, 53];

            Assert.Equal([52], LocalEpisodeNumbers.FindGaps(numbers));
        }

        [Fact]
        public void FindGaps_RealGap_IsReported()
        {
            // Verifiziert am Dateibestand: 226 und 228 liegen vor, 227 fehlt tatsächlich.
            IReadOnlyList<int> numbers = [225, 226, 228];

            Assert.Equal([227], LocalEpisodeNumbers.FindGaps(numbers));
        }

        [Fact]
        public void FindGaps_NoNumbers_ReturnsEmpty()
        {
            Assert.Empty(LocalEpisodeNumbers.FindGaps([]));
        }

        [Fact]
        public void Highest_UsesAllPatterns()
        {
            string[] folders = ["001 - Erste", "Folge 007 - Siebte", "003"];

            Assert.Equal(7, LocalEpisodeNumbers.Highest(folders));
        }

        [Fact]
        public void Highest_NoNumbers_ReturnsZero()
        {
            Assert.Equal(0, LocalEpisodeNumbers.Highest(["Sammlung", "Bonusmaterial"]));
        }

        [Theory]
        [InlineData("1984 - Orwell", null)]      // Jahreszahl im Titel, keine Folge 198
        [InlineData("045a - Sonderfolge", 45)]   // Buchstaben-Suffix
        [InlineData("012-013 - Doppelfolge", 12)] // Doppelfolge: erste Nummer zählt
        [InlineData("Kinderparty", null)]
        public void ExtractNumber_EdgeCases(string folderName, int? expected)
        {
            Assert.Equal(expected, LocalEpisodeNumbers.ExtractNumber(folderName));
        }
    }
}
