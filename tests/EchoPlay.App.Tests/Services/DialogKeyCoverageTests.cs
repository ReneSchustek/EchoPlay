using EchoPlay.App.Tests.Infrastructure;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Hält die Liste der ausblendbaren Dialoge und die Sprachdateien zusammen.
    /// Ein neuer <see cref="DialogKey"/> ohne Namen stünde in den Einstellungen als
    /// <c>[?DialogName_Xyz?]</c> — sichtbar erst zur Laufzeit und nur dem, der ihn
    /// ausgeblendet hat.
    /// </summary>
    public sealed class DialogKeyCoverageTests
    {
        [Theory]
        [InlineData("de")]
        [InlineData("en-US")]
        public void JederDialogHatEinenNamenInDerSprachdatei(string culture)
        {
            HashSet<string> keys = LoadKeys(culture);

            string[] missing = AllKeys()
                .Select(key => $"DialogName_{key}")
                .Where(name => !keys.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(missing.Length == 0,
                $"Ohne Namen in {culture}: {string.Join(", ", missing)}");
        }

        [Theory]
        [InlineData("de")]
        [InlineData("en-US")]
        public void KeinNameOhneZugehoerigenDialog(string culture)
        {
            HashSet<string> expected = AllKeys().Select(key => $"DialogName_{key}").ToHashSet(StringComparer.Ordinal);

            string[] orphaned = LoadKeys(culture)
                .Where(name => name.StartsWith("DialogName_", StringComparison.Ordinal))
                .Where(name => !expected.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(orphaned.Length == 0,
                $"Namen ohne Dialog in {culture}: {string.Join(", ", orphaned)}");
        }

        [Theory]
        [InlineData("de")]
        [InlineData("en-US")]
        public void DieTexteDesHaekchensStehenInBeidenSprachen(string culture)
        {
            HashSet<string> keys = LoadKeys(culture);

            Assert.Contains("DialogSuppressHintCheckBox", keys);
            Assert.Contains("DialogSuppressConfirmCheckBox", keys);
        }

        [Fact]
        public void NoneIstKeinAusblendbarerDialog()
        {
            // DialogKey.None steht für die Dialoge, die sich nicht ausblenden lassen —
            // er darf deshalb weder einen Namen noch einen Platz in der Liste bekommen.
            Assert.DoesNotContain($"DialogName_{DialogKey.None}", LoadKeys("de"));
        }

        private static IEnumerable<DialogKey> AllKeys() =>
            Enum.GetValues<DialogKey>().Where(key => key != DialogKey.None);

        private static HashSet<string> LoadKeys(string culture)
        {
            XDocument doc = XDocument.Load(ResourcePathResolver.Resolve(culture));
            return doc.Root!
                .Elements("data")
                .Select(entry => entry.Attribute("name")!.Value)
                .ToHashSet(StringComparer.Ordinal);
        }
    }
}
