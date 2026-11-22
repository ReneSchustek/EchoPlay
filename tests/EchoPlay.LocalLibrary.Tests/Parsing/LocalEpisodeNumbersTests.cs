using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Tests.Parsing
{
    /// <summary>
    /// Tests für <see cref="LocalEpisodeNumbers"/>.
    /// Die Testdaten stammen aus einer echten Sammlung mit 92 Serienordnern; jeder Fall hier
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
            // Ordner ohne Titelteil: verlangte jedes Muster einen Titel, gälten in der
            // gemessenen Sammlung 38 von 67 vorhandenen Folgen einer Serie als fehlend.
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
            // Gewänne ein einziges Muster für die ganze Serie, zählten abweichend
            // benannte Ordner als fehlend.
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
            // entstünden 812 erfundene Lücken.
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
            // plausibel, verworfen wird nur, was nicht zur Sammlungsgröße passt.
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

        [Fact]
        public void Assign_SpecialFoldersInNumberedSeries_GetNoNumber()
        {
            // Echte Ordner aus „Die drei Ausrufezeichen". Die vier Sonderordner trugen in der
            // Datenbank die Nummern 87 bis 90, weil durchgezählt wurde; der echte Ordner 087
            // stand dadurch als 85 da.
            string[] folders =
            [
                "Die drei Ausrufezeichen - 086 - Rätselhafter Raub",
                "Die drei Ausrufezeichen - 087 - Geheimnisvoller Liebestrank",
                "Die drei Ausrufezeichen - 088 - Die Krimi-Verschwörung",
                "Die drei Ausrufezeichen - XXX - Adventskalender 2013 - Engel in Gefahr",
                "Die drei Ausrufezeichen - XXX - Adventskalender 2014 - Eisprinzessin in Not",
                "Die drei Ausrufezeichen - XXX - Adventskalender 2017 - Das Weihnachtsrätsel",
                "Die drei Ausrufezeichen - XXX - Das Original Hörspiel zum Kinofilm"
            ];

            Assert.Equal([86, 87, 88, null, null, null, null], LocalEpisodeNumbers.Assign(folders, preferred: null));
        }

        [Fact]
        public void Assign_PreferredPatternMatches_WinsOverGeneralPatterns()
        {
            // „038 - 01 - …" ist Folge 38 mit Kapitel 01. Die allgemeinen Muster läsen die 01;
            // das eingestellte Muster des Nutzers weiß es besser.
            string[] folders = ["037 - Pumuckl und die Gummiente", "038 - 01 - Pumuckl und der blaue Klabauter"];

            IReadOnlyList<int?> numbers = LocalEpisodeNumbers.Assign(
                folders,
                name => int.TryParse(name.AsSpan(0, 3), out int number) ? number : null);

            Assert.Equal([37, 38], numbers);
        }

        [Fact]
        public void Assign_PreferredPatternGivesZero_FallsBackToGeneralPatterns()
        {
            // „000 - Adventskalender" ist eine Sonderfolge; 0 ist keine Folgennummer.
            string[] folders = ["001 - Erste", "000 - Adventskalender 2021"];

            IReadOnlyList<int?> numbers = LocalEpisodeNumbers.Assign(
                folders,
                name => int.TryParse(name.AsSpan(0, 3), out int number) ? number : null);

            Assert.Equal([1, null], numbers);
        }

        [Fact]
        public void Assign_NoFolderCarriesANumber_CountsInOrder()
        {
            // Ohne jede Nummer ist die Reihenfolge der Ordner die Nummerierung.
            string[] folders = ["Staffel 1", "Staffel 2", "Bonus"];

            Assert.Equal([1, 2, 3], LocalEpisodeNumbers.Assign(folders, preferred: null));
        }

        [Fact]
        public void Assign_YearsOnly_CountsInOrder()
        {
            // Jahrgänge sind keine Folgennummern; die Serie gilt als unnummeriert.
            string[] folders = ["1995 - Alles verboten", "1996 - Empööörend"];

            Assert.Equal([1, 2], LocalEpisodeNumbers.Assign(folders, preferred: null));
        }

        [Fact]
        public void Assign_LoneNumberTooLargeForCollection_CountsInOrder()
        {
            // „Arsène Lupin - 813" ist ein Romantitel, keine Folge 813 bei drei Ordnern.
            string[] folders = ["Arsène Lupin - 813 - Der Roman", "Der Gentleman-Einbrecher", "Die hohle Nadel"];

            Assert.Equal([1, 2, 3], LocalEpisodeNumbers.Assign(folders, preferred: null));
        }

        [Fact]
        public void Assign_LoneNumberFromPreferredPattern_IsKept()
        {
            // Ein einzelner Ordner „042", den das gewählte Muster erkennt, ist Folge 42.
            IReadOnlyList<int?> numbers = LocalEpisodeNumbers.Assign(
                ["042"],
                name => int.TryParse(name, out int number) ? number : null);

            Assert.Equal([42], numbers);
        }

        [Fact]
        public void Assign_EmptyList_ReturnsEmpty()
        {
            Assert.Empty(LocalEpisodeNumbers.Assign([], preferred: null));
        }

        [Theory]
        [InlineData("1984 - Orwell", null)]      // Jahreszahl im Titel, keine Folge 198
        [InlineData("045a - Sonderfolge", 45)]   // Buchstaben-Suffix
        [InlineData("012-013 - Doppelfolge", 12)] // Doppelfolge: erste Nummer zählt
        [InlineData("Kinderparty", null)]
        [InlineData("Die drei Ausrufezeichen - 066 -Tanz der Herzen", 66)] // fehlendes Leerzeichen nach dem Trenner
        [InlineData("Die drei Ausrufezeichen - XXX - Das Original Hörspiel zum Kinofilm", null)]
        public void ExtractNumber_EdgeCases(string folderName, int? expected)
        {
            Assert.Equal(expected, LocalEpisodeNumbers.ExtractNumber(folderName));
        }
    }
}
