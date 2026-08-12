using EchoPlay.App.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Views
{
    /// <summary>
    /// Hält die Gestaltungslinie mechanisch, statt sie nur zu vereinbaren.
    /// <para>
    /// Zwei Zusagen stehen hier, und die erste ist die wichtigere: EchoPlay führt sechs
    /// Farbpaletten, von denen zur Laufzeit immer nur eine geladen ist. Fehlt ein Schlüssel in
    /// einer davon, bricht die Bindung <em>genau in diesem Theme</em> — und das fällt nicht beim
    /// Bauen auf, sondern beim Nutzer, der zufällig dieses Theme gewählt hat. Vor Einführung
    /// dieser Prüfung führten vier der sechs Paletten 55 Schlüssel und zwei davon 87.
    /// </para>
    /// <para>
    /// Prüft die XAML-Dateien als XML, damit kein WinUI-Host nötig ist.
    /// </para>
    /// </summary>
    public sealed class GestaltungslinieGuardTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// Wörterbücher unter <c>Themes/</c>, die keine Farbpalette sind: <c>Tokens.xaml</c> führt
        /// ausschließlich Form (Abstände, Radien, Größen), <c>Brand.xaml</c> die bewusst
        /// theme-unabhängigen Farben des Startbilds, <c>ControlStyles.xaml</c> die gemeinsamen
        /// Textstile, die ihre Farben nur aus der jeweils geladenen Palette beziehen.
        /// </summary>
        private static readonly string[] NonPaletteDictionaries =
            ["Tokens.xaml", "Brand.xaml", "ControlStyles.xaml"];

        /// <summary>
        /// Attribute, hinter denen eine Farbe steht. Für benannte Farben („White") ist diese
        /// Einschränkung nötig, weil ein Wort wie „Gray" sonst auch in Fließtext anschlüge.
        /// </summary>
        private static readonly string[] ColorAttributes =
        [
            "Foreground", "Background", "BorderBrush", "Fill", "Stroke",
            "Color", "PlaceholderForeground", "SelectionHighlightColor"
        ];

        /// <summary>
        /// <c>Transparent</c> ist keine Farbe der Marke, sondern eine Aussage über die Fläche —
        /// und deshalb ausdrücklich erlaubt.
        /// </summary>
        private static readonly string[] AllowedNamedValues = ["Transparent"];

        private static readonly Regex HexColor =
            new(@"^#([0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        private static readonly Regex NamedColor =
            new(@"^(White|Black|Gray|Grey|Red|Green|Blue|Yellow|Orange|Purple|Pink|Brown|Cyan|Magenta|Lime|Navy|Teal|Olive|Maroon|Silver|Gold|Beige|Ivory|Khaki|Coral|Salmon|Crimson|Indigo|Violet|Turquoise|Tan|Plum|Orchid|Wheat|Snow|Azure|Lavender|Light\w+|Dark\w+|Medium\w+|Pale\w+|Deep\w+)$",
                RegexOptions.Compiled);

        /// <summary>
        /// Die eigentliche Zusage: Jede Palette belegt denselben Satz Schlüssel — und zwar in
        /// beiden Abschnitten, die WinUI kennt.
        /// </summary>
        [Fact]
        public void AllPalettes_ShareTheSameKeySet()
        {
            Dictionary<string, HashSet<string>> paletten = [];

            foreach (string file in EnumeratePaletteFiles())
            {
                XDocument doc = XDocument.Load(file);
                string name = Path.GetFileNameWithoutExtension(file);

                foreach (XElement section in doc.Descendants()
                    .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null))
                {
                    string sectionName = section.Attribute(X + "Key")!.Value;
                    paletten[$"{name}/{sectionName}"] =
                    [
                        .. section.Elements()
                            .Select(e => e.Attribute(X + "Key")?.Value)
                            .Where(k => k is not null)
                            .Select(k => k!)
                    ];
                }
            }

            Assert.True(paletten.Count > 0, "Keine Farbpalette gefunden — der Test prüft sonst nichts.");

            // Die Vereinigungsmenge ist der Maßstab: Was irgendeine Palette führt, müssen alle führen.
            HashSet<string> alle = [.. paletten.Values.SelectMany(v => v)];

            List<string> offenders = [];
            foreach (KeyValuePair<string, HashSet<string>> palette in paletten.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string[] fehlend = [.. alle.Except(palette.Value).OrderBy(k => k, StringComparer.Ordinal)];
                if (fehlend.Length > 0)
                {
                    offenders.Add($"{palette.Key} fehlen {fehlend.Length}: {string.Join(", ", fehlend)}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Farbpaletten mit unvollständigem Schlüsselsatz (die Bindung bricht genau in diesem Theme):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Jede Palette muss beide Abschnitte führen. Fehlt einer, fällt WinUI für dieses Theme
        /// auf seine eigenen Vorgabefarben zurück — sichtbar als schwarze Schrift auf dunklem
        /// Grund, und zwar nur bei dem einen Theme.
        /// </summary>
        [Fact]
        public void EveryPalette_DefinesLightAndDarkSection()
        {
            List<string> offenders = [];

            foreach (string file in EnumeratePaletteFiles())
            {
                XDocument doc = XDocument.Load(file);

                string[] sections = [.. doc.Descendants()
                    .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null)
                    .Select(e => e.Attribute(X + "Key")!.Value)];

                foreach (string erwartet in new[] { "Light", "Dark" })
                {
                    if (!sections.Contains(erwartet, StringComparer.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)} führt keinen Abschnitt \"{erwartet}\"");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Farbpaletten ohne beide Abschnitte:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Wer in einer Ansicht eine Farbe fest hinschreibt, hat die Linie an dieser Stelle
        /// verlassen — und niemand findet es beim nächsten Theme-Wechsel wieder.
        /// </summary>
        [Fact]
        public void NoView_HardCodesAColorValue()
        {
            List<string> offenders = [];

            foreach (string file in EnumerateViewFiles())
            {
                XDocument doc = XDocument.Load(file, LoadOptions.SetLineInfo);

                foreach (XElement element in doc.Descendants())
                {
                    foreach (XAttribute attribute in element.Attributes())
                    {
                        if (!IsColorValue(element, attribute)) continue;

                        offenders.Add(
                            $"{Path.GetFileName(file)}:{LineOf(element)} – {attribute.Name.LocalName}=\"{attribute.Value}\"");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Feste Farbwerte in Ansichten (gehören in die Paletten, nicht in die Seite):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        private static bool IsColorValue(XElement element, XAttribute attribute)
        {
            string wert = attribute.Value.Trim();
            if (wert.Length == 0 || AllowedNamedValues.Contains(wert, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            // Ein Wert dieser Form ist praktisch immer eine Farbe — unabhängig davon, hinter
            // welchem Attribut er steht. Fängt auch Setter-Value und Animationsziele ab.
            if (HexColor.IsMatch(wert)) return true;

            if (!NamedColor.IsMatch(wert)) return false;

            // Benannte Farben nur dort melden, wo tatsächlich eine Farbe erwartet wird.
            string name = attribute.Name.LocalName;
            if (ColorAttributes.Contains(name, StringComparer.Ordinal)) return true;

            // <Setter Property="Foreground" Value="White"/> — die Farbe steht im Value,
            // die Rolle im Property daneben.
            return name == "Value"
                && element.Name.LocalName == "Setter"
                && element.Attribute("Property") is XAttribute property
                && ColorAttributes.Any(c => property.Value.EndsWith(c, StringComparison.Ordinal));
        }

        private static int LineOf(XElement element) =>
            (element as System.Xml.IXmlLineInfo)?.LineNumber ?? 0;

        /// <summary>Die sechs Farbpaletten unter <c>Themes/</c>.</summary>
        private static IEnumerable<string> EnumeratePaletteFiles() =>
            Directory
                .EnumerateFiles(Path.Combine(RepositoryPaths.AppProject(), "Themes"), "*.xaml", SearchOption.TopDirectoryOnly)
                .Where(p => !NonPaletteDictionaries.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);

        /// <summary>
        /// Alles, was Oberfläche ist: Seiten, Bausteine, Fenster. Ohne die Wörterbücher unter
        /// <c>Themes/</c> — dort gehören Farben ausdrücklich hin.
        /// </summary>
        private static IEnumerable<string> EnumerateViewFiles() =>
            Directory
                .EnumerateFiles(RepositoryPaths.AppProject(), "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "Themes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);
    }
}
