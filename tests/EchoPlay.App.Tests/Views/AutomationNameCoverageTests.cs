using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Views
{
    /// <summary>
    /// Hält die Bedienelemente für Screenreader und UI-Automation ansprechbar.
    /// Anlass: Beim Klick-Smoke am 2026-07-27 meldeten alle Kacheln ihren ViewModel-Typnamen
    /// („EchoPlay.App.ViewModels.LocalArtistCardViewModel") und Icon-Buttons gar nichts —
    /// WinUI fällt ohne <c>AutomationProperties.Name</c> auf <c>ToString()</c> zurück.
    /// Prüft die XAML-Dateien als XML, damit kein WinUI-Host nötig ist.
    /// </summary>
    public sealed class AutomationNameCoverageTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void ItemTemplates_HaveAutomationName()
        {
            List<string> offenders = [];

            foreach (string file in EnumerateXamlFiles())
            {
                // SetLineInfo: ohne das meldet der Test Zeile 0 und ist beim Beheben wertlos.
                XDocument doc = XDocument.Load(file, LoadOptions.SetLineInfo);

                foreach (XElement template in doc.Descendants(Xaml + "DataTemplate"))
                {
                    // Nur typisierte Templates prüfen: dort bindet WinUI ein ViewModel,
                    // dessen ToString() sonst als Name durchschlägt.
                    if (template.Attribute(X + "DataType") is null) continue;

                    XElement? root = template.Elements().FirstOrDefault();
                    if (root is null) continue;

                    if (HasAutomationName(root)) continue;

                    offenders.Add($"{Path.GetFileName(file)}:{LineOf(root)} – DataTemplate für {template.Attribute(X + "DataType")!.Value}");
                }
            }

            Assert.True(offenders.Count == 0,
                "DataTemplate-Wurzeln ohne AutomationProperties.Name (Screenreader liest den ViewModel-Typnamen):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void IconOnlyButtons_HaveAccessibleName()
        {
            List<string> offenders = [];

            foreach (string file in EnumerateXamlFiles())
            {
                // SetLineInfo: ohne das meldet der Test Zeile 0 und ist beim Beheben wertlos.
                XDocument doc = XDocument.Load(file, LoadOptions.SetLineInfo);

                foreach (XElement button in doc.Descendants(Xaml + "Button"))
                {
                    if (!IsIconOnly(button)) continue;
                    if (HasAutomationName(button)) continue;

                    // x:Uid genügt: der Name kommt dann aus der .resw
                    // (<Uid>.AutomationProperties.Name) und ist damit lokalisiert.
                    if (button.Attribute(X + "Uid") is not null) continue;

                    offenders.Add($"{Path.GetFileName(file)}: Button mit reinem Icon-Inhalt, Zeile {LineOf(button)}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Icon-Buttons ohne zugänglichen Namen (Tooltip zählt nicht):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        // Attached Properties stehen im XAML ohne Namensraum-Präfix; XLinq liefert sie
        // als Attributnamen "AutomationProperties.Name".
        private static bool HasAutomationName(XElement element) =>
            element.Attribute("AutomationProperties.Name") is not null;

        /// <summary>
        /// Ein Button gilt als „nur Icon", wenn er weder Content-Attribut noch Textinhalt hat
        /// und ausschließlich FontIcon/PathIcon/SymbolIcon/Image enthält.
        /// </summary>
        private static bool IsIconOnly(XElement button)
        {
            XAttribute? content = button.Attribute("Content");
            if (content is not null)
            {
                // Content="&#xE712;" ist ein Glyph, kein Text — Länge 1 als Heuristik.
                return content.Value.Length <= 2;
            }

            List<XElement> children = [.. button.Elements()
                .Where(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal))];

            if (children.Count == 0) return false;

            string[] iconElements = ["FontIcon", "PathIcon", "SymbolIcon", "BitmapIcon", "Image"];
            return children.All(c => iconElements.Contains(c.Name.LocalName, StringComparer.Ordinal));
        }

        private static int LineOf(XElement element) =>
            (element as System.Xml.IXmlLineInfo)?.LineNumber ?? 0;

        private static IEnumerable<string> EnumerateXamlFiles()
        {
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

            return Directory
                .EnumerateFiles(dir.FullName, "*.xaml", SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "EchoPlay.App" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    // App.xaml und Theme-Wörterbücher enthalten keine Bedienelemente.
                    && !p.EndsWith("App.xaml", StringComparison.OrdinalIgnoreCase)
                    && !p.Contains(Path.DirectorySeparatorChar + "Themes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }
}
