using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Tests.Parsing
{
    /// <summary>
    /// Prüft den <see cref="EpisodeFolderParser"/> für verschiedene Ordnermuster und Eingaben.
    /// </summary>
    public sealed class EpisodeFolderParserTests
    {
        /// <summary>
        /// Standardmuster mit führenden Nullen und Titelanteil.
        /// </summary>
        private const string PatternNumberAndTitle = "{number:000} - {title}";

        /// <summary>
        /// Muster ohne Nummer – nur Titel wird extrahiert.
        /// </summary>
        private const string PatternTitleOnly = "{title}";

        [Fact]
        public void Parse_StandardPattern_ExtractsNumberAndTitle()
        {
            EpisodeFolderParser parser = new(PatternNumberAndTitle);

            bool result = parser.TryParse("011 - Das Gespensterschloss", out int? number, out string? title);

            Assert.True(result);
            Assert.Equal(11, number);
            Assert.Equal("Das Gespensterschloss", title);
        }

        [Fact]
        public void Parse_LeadingZeros_ConvertsToInt()
        {
            EpisodeFolderParser parser = new(PatternNumberAndTitle);

            bool result = parser.TryParse("007 - Sherlock Holmes", out int? number, out string? title);

            Assert.True(result);
            Assert.Equal(7, number);
            Assert.Equal("Sherlock Holmes", title);
        }

        [Fact]
        public void Parse_PatternWithoutNumber_ReturnsNullNumber()
        {
            EpisodeFolderParser parser = new(PatternTitleOnly);

            bool result = parser.TryParse("Das Gespensterschloss", out int? number, out string? title);

            Assert.True(result);
            Assert.Null(number);
            Assert.Equal("Das Gespensterschloss", title);
        }

        [Fact]
        public void Parse_NoMatch_ReturnsFalseAndNulls()
        {
            EpisodeFolderParser parser = new(PatternNumberAndTitle);

            // "Extras" passt nicht zum Muster "{number:000} - {title}"
            bool result = parser.TryParse("Extras", out int? number, out string? title);

            Assert.False(result);
            Assert.Null(number);
            Assert.Null(title);
        }

        [Fact]
        public void Parse_TitleWithTrailingSpaces_IsTrimmed()
        {
            EpisodeFolderParser parser = new(PatternNumberAndTitle);

            // .+ matcht Leerzeichen am Ende mit – TryParse muss sie via Trim() entfernen
            bool result = parser.TryParse("001 - Titel mit Leerzeichen  ", out int? number, out string? title);

            Assert.True(result);
            Assert.Equal(1, number);
            Assert.Equal("Titel mit Leerzeichen", title);
        }

        [Fact]
        public void StripLeadingSequenceNumber_RemovesDashSeparatedPrefix()
        {
            // "001 - " ist ein typischer Sortierpräfix in lokalen Bibliotheken
            string result = EpisodeFolderParser.StripLeadingSequenceNumber("001 - Klassenfahrt zur Hexenburg");

            Assert.Equal("Klassenfahrt zur Hexenburg", result);
        }

        [Fact]
        public void StripLeadingSequenceNumber_RemovesDotSeparatedPrefix()
        {
            // "01." kommt vor bei Tracknamen wie "01. Einleitung"
            string result = EpisodeFolderParser.StripLeadingSequenceNumber("01. Stromausfall");

            Assert.Equal("Stromausfall", result);
        }

        [Fact]
        public void StripLeadingSequenceNumber_RemovesSpaceSeparatedPrefix()
        {
            // Nur Leerzeichen als Separator – z.B. "42 Titeltext"
            string result = EpisodeFolderParser.StripLeadingSequenceNumber("42 Titeltext");

            Assert.Equal("Titeltext", result);
        }

        [Fact]
        public void StripLeadingSequenceNumber_LeavesNameUnchanged_WhenNoPrefix()
        {
            // Ordnernamen ohne führende Zahl bleiben unverändert
            string result = EpisodeFolderParser.StripLeadingSequenceNumber("Das Gespensterschloss");

            Assert.Equal("Das Gespensterschloss", result);
        }

        [Fact]
        public void StripLeadingSequenceNumber_HandlesDoublePrefixCorrectly()
        {
            // Praxisfall: "001 - 116 Klassenfahrt" → "001 - " wird gestripped, "116 " bleibt
            // (nur der erste Präfix wird entfernt – kein rekursives Stripping)
            string result = EpisodeFolderParser.StripLeadingSequenceNumber("001 - 116 Klassenfahrt");

            Assert.Equal("116 Klassenfahrt", result);
        }

        [Fact]
        public void TryParse_VerarbeitetLangenInputInUnter500ms()
        {
            // Schutzschranke: Auch bei bewusst langen Eingaben darf der Parser nicht hängen.
            EpisodeFolderParser parser = new("{*} - {number:000} - {title}");
            string longInput = new string('-', 5_000) + " 001 - Titel";

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _ = parser.TryParse(longInput, out int? _, out string? _);
            stopwatch.Stop();

            Assert.True(
                stopwatch.ElapsedMilliseconds < 500,
                $"TryParse benötigte {stopwatch.ElapsedMilliseconds} ms – Regex-Timeout greift nicht.");
        }
    }
}
