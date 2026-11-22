using System.Text.RegularExpressions;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Hält die Regel zum Abschalten von Analysatorwarnungen im ganzen Quellbaum fest:
    /// <c>#pragma warning disable</c> ist verboten, <c>[SuppressMessage]</c> nur mit Begründung.
    /// </summary>
    /// <remarks>
    /// Die Migrationen sind ausgenommen (siehe <see cref="SourceTreeFiles"/>); sie bringen ihre
    /// Abschaltungen aus <c>dotnet ef</c> selbst mit.
    /// </remarks>
    public sealed partial class WarningSuppressionGuardTests
    {
        // Zusammengesetzt, weil der Quelltext-Prüfer der Werkstatt per Textsuche arbeitet und
        // diese Datei sonst selbst für eine Abschaltung hielte.
        private const string PragmaDisable = "#pragma" + " warning disable";

        [Fact]
        public void SourceTree_ContainsNoPragmaWarningDisable()
        {
            List<string> findings = [];

            foreach (string file in EnumerateSourceFiles())
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith(PragmaDisable, StringComparison.Ordinal))
                    {
                        findings.Add($"{Relative(file)}:{i + 1}");
                    }
                }
            }

            Assert.True(findings.Count == 0,
                PragmaDisable + " ist verboten (zero-warnings.md); Ursache beheben oder mit begründetem SuppressMessage-Attribut abschalten:\n"
                + string.Join('\n', findings));
        }

        [Fact]
        public void EverySuppressMessage_CarriesAJustification()
        {
            List<string> findings = [];

            foreach (string file in EnumerateSourceFiles())
            {
                string text = File.ReadAllText(file);
                foreach (Match attribute in SuppressMessageAttribute().Matches(text))
                {
                    if (!JustificationWithText().IsMatch(attribute.Value))
                    {
                        int line = text.AsSpan(0, attribute.Index).Count('\n') + 1;
                        findings.Add($"{Relative(file)}:{line}");
                    }
                }
            }

            Assert.True(findings.Count == 0,
                "SuppressMessage-Attribut ohne Begründung (zero-warnings.md):\n" + string.Join('\n', findings));
        }

        /// <summary>
        /// Belegt, dass die Suche überhaupt Dateien findet. Ohne diese Probe blieben beide Wächter
        /// nach einem Umzug des Quellbaums grün, weil sie nichts mehr durchsuchen.
        /// </summary>
        [Fact]
        public void Guard_ScansTheRealSourceTree()
        {
            string[] files = [.. EnumerateSourceFiles()];

            Assert.Contains(files, file => file.EndsWith(Path.Combine("Services", "PlayerService.cs"), StringComparison.Ordinal));
            Assert.DoesNotContain(files, file => file.Contains(Path.DirectorySeparatorChar + "Migrations" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }

        private static IEnumerable<string> EnumerateSourceFiles() => SourceTreeFiles.Enumerate();

        private static string Relative(string file) => SourceTreeFiles.Relative(file);

        // Vom Attributnamen bis zur schließenden Klammer des Attributs, über Zeilen hinweg; die
        // Begründungstexte enthalten runde Klammern, deshalb endet der Treffer erst an „)]".
        [GeneratedRegex(@"SuppressMessage\(.*?\)\]", RegexOptions.Singleline)]
        private static partial Regex SuppressMessageAttribute();

        [GeneratedRegex(@"Justification\s*=\s*""[^""\s][^""]*""")]
        private static partial Regex JustificationWithText();
    }
}
