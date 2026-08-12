using EchoPlay.App.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Views
{
    /// <summary>
    /// Misst den Kontrast von Text zu seinem Untergrund in allen Farbpaletten.
    /// <para>
    /// Der Wert 4,5:1 stammt aus der Zugänglichkeitsvorgabe für Fließtext. Er ist hier keine
    /// Empfehlung, sondern eine Zusage: EchoPlay führt sechs Paletten, und eine zu blasse
    /// Kombination fällt nur dem auf, der genau dieses Theme gewählt hat. Von Hand nachmessen
    /// müsste man sechs Paletten mal zwei Abschnitte mal jede Textrolle — das wird nach dem
    /// zweiten Mal niemand tun.
    /// </para>
    /// </summary>
    public sealed class PaletteContrastTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>Wörterbücher unter <c>Themes/</c>, die keine Farbpalette sind.</summary>
        private static readonly string[] NonPaletteDictionaries =
            ["Tokens.xaml", "Brand.xaml", "ControlStyles.xaml"];

        /// <summary>Mindestkontrast für Text nach der Zugänglichkeitsvorgabe.</summary>
        private const double MinimumTextContrast = 4.5;

        /// <summary>
        /// Mindestkontrast für Bedienelemente. Niedriger als bei Text, weil es hier nicht ums
        /// Lesen geht, sondern darum, einen Zustand überhaupt zu erkennen.
        /// </summary>
        private const double MinimumControlContrast = 3.0;

        /// <summary>
        /// Die Paare aus Textfarbe und Fläche, auf der sie steht. Nur Kombinationen, die in der
        /// Anwendung wirklich vorkommen — ein Test, der jede Farbe gegen jede prüft, meldet
        /// Verstöße, die niemand zu sehen bekommt.
        /// </summary>
        private static readonly (string Foreground, string Background, string Usage)[] TextPairings =
        [
            ("TextPrimaryBrush", "AppBackgroundBrush", "Fließtext auf der Seite"),
            ("TextPrimaryBrush", "SurfaceBrush", "Fließtext auf einer Fläche"),
            ("TextPrimaryBrush", "CardBackgroundBrush", "Fließtext auf einer Kachel"),
            ("TextSecondaryBrush", "AppBackgroundBrush", "Nebentext auf der Seite"),
            ("TextSecondaryBrush", "SurfaceBrush", "Nebentext auf einer Fläche"),
            ("TextSecondaryBrush", "CardBackgroundBrush", "Nebentext auf einer Kachel"),
            ("TextOnAccentBrush", "AccentPrimaryBrush", "Beschriftung auf der Primäraktion"),
            ("CardHeaderForegroundBrush", "CardHeaderBackgroundBrush", "Kachelkopf"),
            ("CardBodyForegroundBrush", "CardBodyBackgroundBrush", "Kachelkörper"),
            ("TextPrimaryBrush", "InputBackgroundBrush", "Eingabe im Suchfeld")
        ];

        /// <summary>
        /// Bedienelemente, die sich abheben müssen, weil an ihnen ein Zustand hängt.
        /// <para>
        /// Der <em>ruhende</em> Rahmen eines Eingabefelds steht bewusst nicht in dieser Liste:
        /// Die Gestaltungslinie schreibt dafür einen Hairline-Rahmen mit 8 bis 10 Prozent
        /// Deckkraft vor, der 3:1 nicht erreichen kann. Erkennbar bleibt das Feld dort über
        /// Lupensymbol und Platzhalter. Der <em>Fokus</em> dagegen ist reine Zustandsanzeige —
        /// wer ihn nicht sieht, weiß beim Tippen nicht, wohin er schreibt.
        /// </para>
        /// </summary>
        private static readonly (string Foreground, string Background, string Usage)[] ControlPairings =
        [
            ("InputBorderFocusBrush", "InputBackgroundBrush", "Fokusrahmen des Suchfelds"),
            ("AccentPrimaryBrush", "SurfaceBrush", "Rand des aktiven Filter-Chips"),
            ("AccentPrimaryBrush", "AppBackgroundBrush", "Rand des angesprungenen Buchstabens"),
            ("SelectionBorderBrush", "CardBackgroundBrush", "Rand der gewählten Kachel")
        ];

        [Fact]
        public void EveryPalette_KeepsTextReadableOnItsBackground() =>
            AssertEveryPaletteMeets(TextPairings, MinimumTextContrast,
                "Text unter {0:F1}:1 Kontrast (in genau diesem Theme schwer zu lesen):");

        /// <summary>
        /// Bedienelemente brauchen weniger Kontrast als Text, aber nicht keinen: Was man nicht
        /// vom Grund unterscheiden kann, findet man auch nicht.
        /// </summary>
        [Fact]
        public void EveryPalette_KeepsControlStatesDistinguishable() =>
            AssertEveryPaletteMeets(ControlPairings, MinimumControlContrast,
                "Zustandsanzeigen unter {0:F1}:1 Kontrast (vom Untergrund nicht zu unterscheiden):");

        /// <summary>
        /// Misst alle übergebenen Paare in jeder Palette und jedem Abschnitt gegen die Schwelle.
        /// </summary>
        /// <param name="pairings">Die zu prüfenden Farbpaare.</param>
        /// <param name="minimum">Geforderter Mindestkontrast.</param>
        /// <param name="messageFormat">Kopfzeile der Fehlermeldung mit <c>{0}</c> für die Schwelle.</param>
        private static void AssertEveryPaletteMeets(
            (string Foreground, string Background, string Usage)[] pairings,
            double minimum,
            string messageFormat)
        {
            List<string> offenders = [];
            int measuredPairs = 0;

            foreach (string file in EnumeratePaletteFiles())
            {
                string paletteName = Path.GetFileNameWithoutExtension(file);
                XDocument document = XDocument.Load(file);

                foreach (XElement dictionary in document.Descendants()
                    .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null))
                {
                    string sectionName = dictionary.Attribute(X + "Key")!.Value;
                    Dictionary<string, string> brushes = ReadBrushes(dictionary);

                    foreach ((string foregroundKey, string backgroundKey, string usage) in pairings)
                    {
                        if (!brushes.TryGetValue(foregroundKey, out string? foreground)
                            || !brushes.TryGetValue(backgroundKey, out string? background))
                        {
                            // Fehlende Schlüssel meldet AllPalettes_ShareTheSameKeySet — hier
                            // wäre es nur dieselbe Meldung ein zweites Mal.
                            continue;
                        }

                        double ratio = ContrastRatio(foreground, background);
                        measuredPairs++;

                        if (ratio < minimum)
                        {
                            offenders.Add(string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}/{1}: {2} — {3} auf {4} nur {5:F2}:1",
                                paletteName, sectionName, usage, foregroundKey, backgroundKey, ratio));
                        }
                    }
                }
            }

            Assert.True(measuredPairs > 0, "Kein Farbpaar gemessen — der Test prüft sonst nichts.");

            Assert.True(offenders.Count == 0,
                string.Format(CultureInfo.InvariantCulture, messageFormat, minimum)
                + Environment.NewLine + string.Join(Environment.NewLine, offenders.OrderBy(o => o, StringComparer.Ordinal)));
        }

        /// <summary>
        /// Liest alle <c>SolidColorBrush</c>-Einträge eines Abschnitts als Schlüssel und Farbwert.
        /// </summary>
        private static Dictionary<string, string> ReadBrushes(XElement dictionary)
        {
            Dictionary<string, string> brushes = [];

            foreach (XElement brush in dictionary.Elements().Where(e => e.Name.LocalName == "SolidColorBrush"))
            {
                string? key = brush.Attribute(X + "Key")?.Value;
                string? color = brush.Attribute("Color")?.Value;

                if (key is not null && color is not null && color.StartsWith('#'))
                {
                    brushes[key] = color;
                }
            }

            return brushes;
        }

        /// <summary>
        /// Berechnet das Kontrastverhältnis zweier Farben nach der Zugänglichkeitsvorgabe.
        /// </summary>
        /// <returns>Ein Wert zwischen 1 (kein Unterschied) und 21 (Schwarz auf Weiß).</returns>
        private static double ContrastRatio(string foreground, string background)
        {
            double lighter = RelativeLuminance(foreground);
            double darker = RelativeLuminance(background);

            if (lighter < darker)
            {
                (lighter, darker) = (darker, lighter);
            }

            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>
        /// Relative Helligkeit einer Farbe. Die Gewichte tragen dem Auge Rechnung, das Grün
        /// deutlich heller wahrnimmt als Blau.
        /// </summary>
        private static double RelativeLuminance(string hex)
        {
            (byte red, byte green, byte blue) = ParseRgb(hex);

            return (0.2126 * Channel(red)) + (0.7152 * Channel(green)) + (0.0722 * Channel(blue));
        }

        private static double Channel(byte value)
        {
            double share = value / 255.0;

            return share <= 0.03928
                ? share / 12.92
                : Math.Pow((share + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Zerlegt <c>#RRGGBB</c> oder <c>#AARRGGBB</c> in seine Farbanteile.
        /// </summary>
        /// <remarks>
        /// Ein Alphawert wird übersprungen und nicht verrechnet: Halbdurchsichtige Farben
        /// liegen in diesen Paletten nicht als Text- oder Flächenfarbe vor, und eine
        /// Vermischung mit dem Untergrund zu raten wäre schlechter, als sie wegzulassen.
        /// </remarks>
        private static (byte Red, byte Green, byte Blue) ParseRgb(string hex)
        {
            string digits = hex.TrimStart('#');

            if (digits.Length == 8)
            {
                digits = digits[2..];
            }

            if (digits.Length == 3)
            {
                digits = string.Concat(digits.Select(c => new string(c, 2)));
            }

            return (
                byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        private static IEnumerable<string> EnumeratePaletteFiles() =>
            Directory
                .EnumerateFiles(Path.Combine(RepositoryPaths.AppProject(), "Themes"), "*.xaml", SearchOption.TopDirectoryOnly)
                .Where(p => !NonPaletteDictionaries.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);
    }
}
