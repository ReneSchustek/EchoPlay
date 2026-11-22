using EchoPlay.App.Tests.Infrastructure;
using System.Xml.Linq;

namespace EchoPlay.App.Tests.Views
{
    /// <summary>
    /// Findet die Farbpaletten unter <c>Themes/</c> und ihre benannten Abschnitte. Die Prüfungen
    /// der Gestaltungslinie und des Kontrasts nutzen beide diese Liste, damit sie dieselben Dateien
    /// sehen; zwei Kopien liefen sonst beim nächsten neuen Wörterbuch auseinander.
    /// </summary>
    internal static class PaletteFiles
    {
        /// <summary>Namensraum der <c>x:Key</c>-Attribute.</summary>
        public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// Wörterbücher unter <c>Themes/</c>, die keine Farbpalette sind: <c>Tokens.xaml</c> führt
        /// ausschließlich Form (Abstände, Radien, Größen), <c>Brand.xaml</c> die
        /// theme-unabhängigen Farben des Startbilds, <c>ControlStyles.xaml</c> die gemeinsamen
        /// Textstile, die ihre Farben nur aus der jeweils geladenen Palette beziehen.
        /// </summary>
        private static readonly string[] NonPaletteDictionaries =
            ["Tokens.xaml", "Brand.xaml", "ControlStyles.xaml"];

        /// <summary>Die sechs Farbpaletten unter <c>Themes/</c>, nach Namen geordnet.</summary>
        public static IEnumerable<string> Enumerate() =>
            Directory
                .EnumerateFiles(Path.Combine(RepositoryPaths.AppProject(), "Themes"), "*.xaml", SearchOption.TopDirectoryOnly)
                .Where(p => !NonPaletteDictionaries.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);

        /// <summary>
        /// Jeder benannte Abschnitt (etwa <c>Light</c> oder <c>Dark</c>) jeder Palette, zusammen
        /// mit dem Namen der Palette.
        /// </summary>
        public static IEnumerable<(string Palette, string Section, XElement Dictionary)> EnumerateSections()
        {
            foreach (string file in Enumerate())
            {
                string palette = Path.GetFileNameWithoutExtension(file);
                foreach (XElement dictionary in XDocument.Load(file).Descendants()
                    .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null))
                {
                    yield return (palette, dictionary.Attribute(X + "Key")!.Value, dictionary);
                }
            }
        }
    }
}
