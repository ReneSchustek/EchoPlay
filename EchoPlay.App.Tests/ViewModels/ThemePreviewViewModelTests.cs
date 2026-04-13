using EchoPlay.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="ThemePreviewViewModel"/>.
    /// Stellt sicher, dass alle Themes korrekt definiert sind und die Farbwerte
    /// mit den ResourceDictionary-Dateien übereinstimmen.
    /// </summary>
    public sealed class ThemePreviewViewModelTests
    {
        [Fact]
        public void All_Returns_Exactly_Six_Themes()
        {
            // Sechs Themes sind konfiguriert – wenn eines fehlt oder eines zu viel ist, stimmt etwas nicht
            IReadOnlyList<ThemePreviewViewModel> themes = ThemePreviewViewModel.All;

            Assert.Equal(6, themes.Count);
        }

        [Fact]
        public void All_Contains_All_Known_ThemeNames()
        {
            // Alle sechs Theme-Tags müssen vorhanden sein
            IReadOnlyList<ThemePreviewViewModel> themes = ThemePreviewViewModel.All;

            Assert.Contains(themes, t => t.Tag == "Ruhrcoder");
            Assert.Contains(themes, t => t.Tag == "ModernClassic");
            Assert.Contains(themes, t => t.Tag == "PaperCoffee");
            Assert.Contains(themes, t => t.Tag == "MidnightLibrary");
            Assert.Contains(themes, t => t.Tag == "ForestSignal");
            Assert.Contains(themes, t => t.Tag == "AmberWhiskey");
        }

        [Fact]
        public void All_Tags_Are_Unique()
        {
            // Keine doppelten Tags – sonst würde SyncThemeRadioButton das falsche Theme auswählen
            IReadOnlyList<ThemePreviewViewModel> themes = ThemePreviewViewModel.All;

            int distinctCount = themes.Select(t => t.Tag).Distinct().Count();

            Assert.Equal(themes.Count, distinctCount);
        }

        [Fact]
        public void All_HaveValidHexColors()
        {
            // Alle Farbwerte müssen als Hex-String vorliegen (#RRGGBB)
            foreach (ThemePreviewViewModel theme in ThemePreviewViewModel.All)
            {
                Assert.StartsWith("#", theme.BackgroundHex, StringComparison.Ordinal);
                Assert.StartsWith("#", theme.PaneHex, StringComparison.Ordinal);
                Assert.StartsWith("#", theme.AccentHex, StringComparison.Ordinal);
                Assert.Equal(7, theme.BackgroundHex.Length);
                Assert.Equal(7, theme.PaneHex.Length);
                Assert.Equal(7, theme.AccentHex.Length);
            }
        }

        [Fact]
        public void All_HaveNonEmptyDisplayNames()
        {
            foreach (ThemePreviewViewModel theme in ThemePreviewViewModel.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(theme.DisplayName));
            }
        }

        [Theory]
        [InlineData("Ruhrcoder",       "#EEEEEE", "#2F4858", "#2E6DA4")]
        [InlineData("MidnightLibrary", "#12141D",  "#1B1E2B", "#5B4DFF")]
        [InlineData("PaperCoffee",     "#FFFBF2",  "#FCF5E5", "#A06040")]
        public void Theme_ColorsMatchResourceDictionary(
            string tag, string expectedBg, string expectedPane, string expectedAccent)
        {
            // Stichprobenprüfung: Farben müssen mit den Werten aus den Theme-XAML-Dateien übereinstimmen
            ThemePreviewViewModel? theme = ThemePreviewViewModel.All.FirstOrDefault(t => t.Tag == tag);

            Assert.NotNull(theme);
            Assert.Equal(expectedBg, theme!.BackgroundHex);
            Assert.Equal(expectedPane, theme.PaneHex);
            Assert.Equal(expectedAccent, theme.AccentHex);
        }
    }
}
