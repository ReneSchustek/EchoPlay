using EchoPlay.App.Tests.Infrastructure;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Erzwingt die Lokalisierungs-Hygiene der App-Ressourcen: Symmetrie zwischen
    /// den Sprachdateien, keine Leerwerte, keine doppelten Schlüssel.
    /// Liest die <c>Resources.resw</c>-Dateien direkt als XML, damit der Test
    /// unabhängig vom WinUI-<c>ResourceLoader</c> läuft.
    /// </summary>
    public sealed class LocalizationCoverageTests
    {
        private static readonly string DeResourcePath = ResourcePathResolver.Resolve("de");
        private static readonly string EnResourcePath = ResourcePathResolver.Resolve("en-US");

        [Fact]
        public void ResourceKeys_AreSymmetricBetweenDeAndEn()
        {
            HashSet<string> deKeys = LoadKeys(DeResourcePath);
            HashSet<string> enKeys = LoadKeys(EnResourcePath);

            string[] onlyInDe = deKeys.Except(enKeys).OrderBy(k => k).ToArray();
            string[] onlyInEn = enKeys.Except(deKeys).OrderBy(k => k).ToArray();

            Assert.True(onlyInDe.Length == 0,
                $"Keys nur in DE-Ressourcen: {string.Join(", ", onlyInDe)}");
            Assert.True(onlyInEn.Length == 0,
                $"Keys nur in EN-Ressourcen: {string.Join(", ", onlyInEn)}");
        }

        [Fact]
        public void ResourceValues_AreNonEmpty()
        {
            AssertNoEmptyValues(DeResourcePath, "DE");
            AssertNoEmptyValues(EnResourcePath, "EN");
        }

        /// <summary>
        /// Schlüssel, die eine Quelldatei liest oder als <c>x:Uid</c> setzt, ohne dass die
        /// Ressourcen sie führen; je Fund mit dem Dateinamen.
        /// </summary>
        private static IEnumerable<string> FindMissingKeys(string file, HashSet<string> keys, HashSet<string> bases)
        {
            string text = File.ReadAllText(file);
            foreach (Match match in CodeKeyPattern.Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!keys.Contains(key) && !bases.Contains(key))
                {
                    yield return $"{Path.GetFileName(file)}: {key}";
                }
            }

            foreach (Match match in UidPattern.Matches(text))
            {
                if (!bases.Contains(match.Groups[1].Value))
                {
                    yield return $"{Path.GetFileName(file)}: x:Uid {match.Groups[1].Value}";
                }
            }
        }

        [Fact]
        public void ReferencedKeys_ExistInResources()
        {
            // Ein fehlender Schlüssel fällt im Betrieb nicht auf: Der Lader liefert dann einen
            // leeren Text, und die Stelle bleibt stumm leer. So stand der Kopf der Suchseite
            // ohne Titel da, weil der Code einen anderen Namen las als die Datei führte.
            HashSet<string> keys = LoadKeys(DeResourcePath);
            HashSet<string> bases = keys.Select(k => k.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
            string appDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(DeResourcePath)!, "..", ".."));

            List<string> missing = [];
            foreach (string file in Directory.EnumerateFiles(appDirectory, "*.*", SearchOption.AllDirectories))
            {
                bool isSource = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
                if (!isSource || IsBuildOutput(file))
                {
                    continue;
                }

                missing.AddRange(FindMissingKeys(file, keys, bases));
            }

            Assert.True(missing.Count == 0, $"Schlüssel ohne Eintrag in den Ressourcen: {string.Join(", ", missing.Distinct())}");
        }

        /// <summary>Schlüssel in den Lese-Aufrufen des Codes, jeweils als erstes Argument in Anführungszeichen.</summary>
        private static readonly Regex CodeKeyPattern = new(
            "(?:SafeResourceLoader\\.Get|\\.GetString|[lL]ocalization(?:Service)?\\??\\.Get)\\(\\s*\"([A-Za-z0-9_.]+)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        /// <summary>Die x:Uid-Kennungen im Markup.</summary>
        private static readonly Regex UidPattern = new("x:Uid=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));

        private static bool IsBuildOutput(string file)
            => file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || file.Contains(Path.DirectorySeparatorChar + "AppPackages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void ResourceKeys_AreUnique()
        {
            AssertNoDuplicateKeys(DeResourcePath, "DE");
            AssertNoDuplicateKeys(EnResourcePath, "EN");
        }

        private static HashSet<string> LoadKeys(string path)
        {
            XDocument doc = XDocument.Load(path);
            return doc.Root!
                .Elements("data")
                .Select(d => d.Attribute("name")!.Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static void AssertNoEmptyValues(string path, string culture)
        {
            XDocument doc = XDocument.Load(path);
            string[] empty = doc.Root!
                .Elements("data")
                .Where(d => string.IsNullOrWhiteSpace(d.Element("value")?.Value))
                .Select(d => d.Attribute("name")!.Value)
                .OrderBy(k => k)
                .ToArray();

            Assert.True(empty.Length == 0,
                $"Leere oder fehlende Werte in {culture}: {string.Join(", ", empty)}");
        }

        private static void AssertNoDuplicateKeys(string path, string culture)
        {
            XDocument doc = XDocument.Load(path);
            string[] duplicates = doc.Root!
                .Elements("data")
                .GroupBy(d => d.Attribute("name")!.Value, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(k => k)
                .ToArray();

            Assert.True(duplicates.Length == 0,
                $"Doppelte Schlüssel in {culture}: {string.Join(", ", duplicates)}");
        }

    }
}
