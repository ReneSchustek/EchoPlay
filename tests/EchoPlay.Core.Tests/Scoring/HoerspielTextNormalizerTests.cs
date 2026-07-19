using EchoPlay.Core.Scoring;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="HoerspielTextNormalizer"/>.
    /// Prüft Umlautkonvertierung, Sonderzeichen-Entfernung und Groß-/Kleinschreibung.
    /// </summary>
    public sealed class HoerspielTextNormalizerTests
    {
        [Fact]
        public void Normalize_UppercaseInput_ConvertsToLowercase()
        {
            // Großbuchstaben müssen in Kleinbuchstaben umgewandelt werden
            string result = HoerspielTextNormalizer.Normalize("TKKG");

            Assert.Equal("tkkg", result);
        }

        [Fact]
        public void Normalize_InputContainsUmlautA_ReplacesWithAe()
        {
            // ä → ae (Standard-ASCII-Schreibweise)
            string result = HoerspielTextNormalizer.Normalize("Ärger");

            Assert.Equal("aerger", result);
        }

        [Fact]
        public void Normalize_ReplacesUmlautOe()
        {
            // ö → oe
            string result = HoerspielTextNormalizer.Normalize("Löwe");

            Assert.Equal("loewe", result);
        }

        [Fact]
        public void Normalize_ReplacesUmlautUe()
        {
            // ü → ue
            string result = HoerspielTextNormalizer.Normalize("Über");

            Assert.Equal("ueber", result);
        }

        [Fact]
        public void Normalize_ReplacesSzligWithSs()
        {
            // ß → ss
            string result = HoerspielTextNormalizer.Normalize("Straße");

            Assert.Equal("strasse", result);
        }

        [Fact]
        public void Normalize_RemovesSpecialCharacters()
        {
            // Satzzeichen und Sonderzeichen werden entfernt
            string result = HoerspielTextNormalizer.Normalize("Die drei ???");

            Assert.Equal("die drei ", result);
        }

        [Fact]
        public void Normalize_InputContainsDigits_PreservesDigits()
        {
            // Ziffern bleiben erhalten
            string result = HoerspielTextNormalizer.Normalize("TKKG Folge 42");

            Assert.Equal("tkkg folge 42", result);
        }

        [Fact]
        public void Normalize_PreservesSpaces()
        {
            // Leerzeichen bleiben erhalten – sie trennen Wörter
            string result = HoerspielTextNormalizer.Normalize("Die drei Fragezeichen");

            Assert.Equal("die drei fragezeichen", result);
        }

        [Fact]
        public void Normalize_HandlesAllUmlautsAtOnce()
        {
            // Alle Umlaute in einem String
            string result = HoerspielTextNormalizer.Normalize("äöüÄÖÜß");

            Assert.Equal("aeoeueaeoeuess", result);
        }

        [Fact]
        public void Normalize_EmptyString_ReturnsEmpty()
        {
            // Leerer String bleibt leer
            string result = HoerspielTextNormalizer.Normalize(string.Empty);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Normalize_EnablesCaseInsensitiveComparison()
        {
            // Normalisierung ermöglicht Vergleich unabhängig von Schreibweise
            string a = HoerspielTextNormalizer.Normalize("Die Drei ???");
            string b = HoerspielTextNormalizer.Normalize("die drei ???");

            Assert.Equal(a, b);
        }
    }
}
