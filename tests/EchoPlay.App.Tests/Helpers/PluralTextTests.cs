using EchoPlay.App.Helpers;
using EchoPlay.App.Tests.Fakes;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für die Auswahl zwischen Singular- und Plural-Ressource. Ohne diese Auswahl
    /// stünde bei einer Anzahl von eins die Mehrzahlform in der Oberfläche.
    /// </summary>
    public sealed class PluralTextTests
    {
        private const string SingularKey = "TestMengeSingular";
        private const string PluralKey = "TestMengePlural";
        private const string SingularFallback = "{0} Datei wird gespeichert.";
        private const string PluralFallback = "{0} Dateien werden gespeichert.";

        [Fact]
        public void Pattern_WithServiceAndOne_UsesSingularKey()
        {
            FakeLocalizationService localization = new(new Dictionary<string, string>
            {
                [SingularKey] = "{0} file will be saved."
            });

            string pattern = PluralText.Pattern(
                localization, 1, SingularKey, PluralKey, SingularFallback, PluralFallback);

            Assert.Equal("{0} file will be saved.", pattern);
        }

        [Fact]
        public void Pattern_WithServiceAndSeveral_UsesPluralKey()
        {
            FakeLocalizationService localization = new(new Dictionary<string, string>
            {
                [PluralKey] = "{0} files will be saved."
            });

            string pattern = PluralText.Pattern(
                localization, 4, SingularKey, PluralKey, SingularFallback, PluralFallback);

            Assert.Equal("{0} files will be saved.", pattern);
        }

        [Fact]
        public void Pattern_WithoutService_UsesFallbacks()
        {
            // Ohne Lokalisierungsdienst (Unit-Test-Pfad) greifen die eingebauten Texte.
            Assert.Equal(
                SingularFallback,
                PluralText.Pattern(null, 1, SingularKey, PluralKey, SingularFallback, PluralFallback));
            Assert.Equal(
                PluralFallback,
                PluralText.Pattern(null, 2, SingularKey, PluralKey, SingularFallback, PluralFallback));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(17)]
        public void Pattern_WithAnyCountButOne_UsesPluralForm(int count)
        {
            // Null gehört zur Mehrzahl: „0 Dateien werden gespeichert" ist richtig,
            // „0 Datei wird gespeichert" nicht.
            string pattern = PluralText.Pattern(
                null, count, SingularKey, PluralKey, SingularFallback, PluralFallback);

            Assert.Equal(PluralFallback, pattern);
        }

        [Fact]
        public void Pattern_WithoutResourceHost_UsesFallbacks()
        {
            // Die Überladung ohne Dienst liest über den SafeResourceLoader. Im Testhost
            // gibt es keine WinUI-Ressourcen, also muss der Fallback greifen.
            Assert.Equal(
                SingularFallback,
                PluralText.Pattern(1, SingularKey, PluralKey, SingularFallback, PluralFallback));
            Assert.Equal(
                PluralFallback,
                PluralText.Pattern(3, SingularKey, PluralKey, SingularFallback, PluralFallback));
        }
    }
}
