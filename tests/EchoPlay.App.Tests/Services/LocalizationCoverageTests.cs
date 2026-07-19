using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

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
        private static readonly string DeResourcePath = ResolveResourcePath("de");
        private static readonly string EnResourcePath = ResolveResourcePath("en-US");

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

        private static string ResolveResourcePath(string culture)
        {
            // Ausgehend vom Solution-Root das App-Projekt suchen, statt den
            // Ordner-Layout (src/) fest zu verdrahten — so übersteht der Test Umzüge.
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? dir = new(baseDir);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EchoPlay.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException($"EchoPlay.slnx nicht gefunden, ausgehend von '{baseDir}'.");
            }

            string relativePath = Path.Combine("Strings", culture, "Resources.resw");
            string path = Directory
                .EnumerateFiles(dir.FullName, "Resources.resw", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.EndsWith(Path.DirectorySeparatorChar + relativePath, StringComparison.OrdinalIgnoreCase)
                    && p.Contains(Path.DirectorySeparatorChar + "EchoPlay.App" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "EchoPlay.App.Tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Ressource-Datei '{relativePath}' nicht gefunden unterhalb von '{dir.FullName}'.");

            return path;
        }
    }
}
