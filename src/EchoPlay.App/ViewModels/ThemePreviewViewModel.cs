using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Vorschau-Daten für ein einzelnes Theme in den Einstellungen.
    /// Die drei Kennfarben (Hintergrund, Seitenleiste, Akzent) werden aus den
    /// ResourceDictionary-Dateien unter <c>Themes/</c> abgeleitet und hier als
    /// Hex-Werte gespeichert, damit die Vorschau-Kacheln die echten Theme-Farben zeigen.
    /// </summary>
    public sealed class ThemePreviewViewModel
    {
        /// <summary>Interner Theme-Name (entspricht dem Dateinamen ohne Erweiterung).</summary>
        public required string Tag { get; init; }

        /// <summary>Anzeigename für die Kachel.</summary>
        public required string DisplayName { get; init; }

        /// <summary>Hintergrundfarbe (ApplicationPageBackgroundThemeBrush) als Hex.</summary>
        public required string BackgroundHex { get; init; }

        /// <summary>Seitenleisten-Farbe (NavigationViewDefaultPaneBackground) als Hex.</summary>
        public required string PaneHex { get; init; }

        /// <summary>Akzentfarbe (AccentFillColorDefaultBrush) als Hex.</summary>
        public required string AccentHex { get; init; }

        /// <summary>
        /// Gibt alle 6 verfügbaren Themes mit ihren Kennfarben zurück.
        /// Die Farben entsprechen den Werten aus den jeweiligen ResourceDictionary-Dateien.
        /// Bei Änderungen an den Theme-Dateien müssen die Werte hier synchron aktualisiert werden.
        /// </summary>
        public static IReadOnlyList<ThemePreviewViewModel> All { get; } =
        [
            new() { Tag = "Ruhrcoder",       DisplayName = "Ruhrcoder",        BackgroundHex = "#EEEEEE", PaneHex = "#2F4858", AccentHex = "#2E6DA4" },
            new() { Tag = "ModernClassic",   DisplayName = "Modern Classic",   BackgroundHex = "#FFFFFF", PaneHex = "#FFFFFF", AccentHex = "#4361EE" },
            new() { Tag = "PaperCoffee",     DisplayName = "Paper Coffee",     BackgroundHex = "#FFFBF2", PaneHex = "#FCF5E5", AccentHex = "#A06040" },
            new() { Tag = "MidnightLibrary", DisplayName = "Midnight Library", BackgroundHex = "#12141D", PaneHex = "#1B1E2B", AccentHex = "#5B4DFF" },
            new() { Tag = "ForestSignal",    DisplayName = "Forest Signal",    BackgroundHex = "#0F1A14", PaneHex = "#16241C", AccentHex = "#3FA37A" },
            new() { Tag = "AmberWhiskey",    DisplayName = "Amber Whiskey",    BackgroundHex = "#1A1410", PaneHex = "#241B16", AccentHex = "#C27A2C" }
        ];
    }
}
