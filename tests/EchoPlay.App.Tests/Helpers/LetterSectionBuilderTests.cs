using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Sichert die Aufteilung der Buchstaben-Abschnitte um den aufgeklappten Folgenbereich ab.
    /// Der Punkt: Was oberhalb steht, was unterhalb weitergeht — und dass der Buchstabe dabei
    /// nur einmal als Überschrift erscheint.
    /// </summary>
    public sealed class LetterSectionBuilderTests
    {
        /// <summary>Kachel-Ersatz für die Tests — mehr als einen Titel braucht die Aufteilung nicht.</summary>
        private sealed class Tile
        {
            public Tile(string title) => Title = title;
            public string Title { get; }
            public override string ToString() => Title;
        }

        private static IReadOnlyList<Tile> Tiles(params string[] titles) =>
            [.. titles.Select(t => new Tile(t))];

        private static string? TitleOf(Tile tile) => tile.Title;

        // Breite für vier Kacheln je Reihe: der Slot ist 148 px breit.
        private const double FourPerRow = 4 * AccordionSplitHelper.SeriesTileSlotWidth;

        private static IEnumerable<string> TitlesOf(LetterSection section) =>
            section.Items.Cast<Tile>().Select(t => t.Title);

        [Fact]
        public void Build_EmptyLibrary_YieldsNothingOnEitherSide()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(Tiles(), TitleOf, -1, FourPerRow);

            Assert.Empty(top);
            Assert.Empty(bottom);
        }

        /// <summary>
        /// Ohne Auswahl klappt nichts auf — dann gibt es auch keine Trennstelle, an der die
        /// Liste zerfallen müsste.
        /// </summary>
        [Fact]
        public void Build_WithoutSelection_PutsEverythingAbove()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(Tiles("Ahoi", "Bibi", "TKKG"), TitleOf, -1, FourPerRow);

            Assert.Equal(["A", "B", "T"], top.Select(s => s.Header));
            Assert.Empty(bottom);
            Assert.All(top, s => Assert.True(s.ShowsHeader));
        }

        /// <summary>
        /// Abschnitte vor der Auswahl bleiben oben, Abschnitte danach wandern unter den
        /// Folgenbereich — sonst stünde die Hälfte des Bestands hinter dem aufgeklappten Teil.
        /// </summary>
        [Fact]
        public void Build_SectionsBeforeAndAfterTheSelectionGoToTheirOwnSide()
        {
            // Auswahl auf "Bibi" (Index 1).
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(Tiles("Ahoi", "Bibi", "TKKG"), TitleOf, 1, FourPerRow);

            Assert.Equal(["A", "B"], top.Select(s => s.Header));
            Assert.Equal(["T"], bottom.Select(s => s.Header));
        }

        /// <summary>
        /// Der geteilte Abschnitt trägt seinen Buchstaben nur oben. Stünde er unten erneut,
        /// läse sich die Fortsetzung wie ein zweiter Abschnitt desselben Buchstabens.
        /// </summary>
        [Fact]
        public void Build_ContinuationBelowCarriesNoHeader()
        {
            // Fünf B-Titel bei vier je Reihe: Auswahl in Reihe 1, ein Titel bleibt unten übrig.
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(
                    Tiles("Bibi", "Benjamin", "Bruno", "Bello", "Bonz"), TitleOf, 0, FourPerRow);

            LetterSection above = Assert.Single(top);
            LetterSection below = Assert.Single(bottom);

            Assert.True(above.ShowsHeader);
            Assert.Equal("B", above.Header);
            Assert.Equal(4, above.Items.Count);

            Assert.False(below.ShowsHeader);
            Assert.Equal(Visibility.Collapsed, below.HeaderVisibility);
            Assert.Equal(["Bonz"], TitlesOf(below));
        }

        /// <summary>
        /// Reicht der Abschnitt genau bis zur Aufklappstelle, entsteht unten kein leerer
        /// Abschnitt — der zöge sonst einen Abstand ohne Inhalt nach sich.
        /// </summary>
        [Fact]
        public void Build_SelectionInLastRow_LeavesNoEmptyContinuation()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(Tiles("Bibi", "Benjamin"), TitleOf, 0, FourPerRow);

            LetterSection above = Assert.Single(top);
            Assert.Equal(2, above.Items.Count);
            Assert.Empty(bottom);
        }

        /// <summary>
        /// Die Aufteilung ändert die Reihenfolge nicht: Die Liste kommt sortiert aus der
        /// Datenschicht, und der Sprung der Buchstabenleiste zielt auf genau diese Ordnung.
        /// </summary>
        [Fact]
        public void Build_KeepsTheGivenOrderAcrossTheSplit()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(
                    Tiles("Bibi", "Benjamin", "Bruno", "Bello", "Bonz", "Bär"), TitleOf, 0, FourPerRow);

            Assert.Equal(["Bibi", "Benjamin", "Bruno", "Bello"], TitlesOf(Assert.Single(top)));
            Assert.Equal(["Bonz", "Bär"], TitlesOf(Assert.Single(bottom)));
        }

        /// <summary>
        /// Umlaute laufen auf ihren Grundbuchstaben — „Ärger" steht unter A und bekommt keinen
        /// eigenen Abschnitt.
        /// </summary>
        [Fact]
        public void Build_DiacriticsShareTheBaseLetterSection()
        {
            (IReadOnlyList<LetterSection> top, _) =
                LetterSectionBuilder.Build(Tiles("Ahoi", "Ärger"), TitleOf, -1, FourPerRow);

            LetterSection only = Assert.Single(top);
            Assert.Equal("A", only.Header);
            Assert.Equal(2, only.Items.Count);
        }

        /// <summary>Ziffern und Zeichen sammeln sich im Fach <c>#</c>.</summary>
        [Fact]
        public void Build_TitlesOutsideTheAlphabetShareTheOtherSection()
        {
            (IReadOnlyList<LetterSection> top, _) =
                LetterSectionBuilder.Build(Tiles("5 Freunde", "3 Fragezeichen"), TitleOf, -1, FourPerRow);

            Assert.Equal("#", Assert.Single(top).Header);
        }

        [Fact]
        public void Build_SelectionIndexOutOfRange_IsTreatedAsNoSelection()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(Tiles("Ahoi", "Bibi"), TitleOf, 99, FourPerRow);

            Assert.Equal(2, top.Count);
            Assert.Empty(bottom);
        }

        /// <summary>
        /// Die Sprachausgabe bekommt je Abschnitt einen eigenen Namen. Ohne Platzhalter in der
        /// Vorlage hießen alle Abschnitte gleich und wären nicht auseinanderzuhalten.
        /// </summary>
        [Fact]
        public void Build_FillsTheLetterIntoTheAutomationName()
        {
            (IReadOnlyList<LetterSection> top, _) =
                LetterSectionBuilder.Build(Tiles("Ahoi", "Bibi"), TitleOf, -1, FourPerRow, "Abschnitt {0}");

            Assert.Equal(["Abschnitt A", "Abschnitt B"], top.Select(s => s.AutomationName));
        }

        [Fact]
        public void Build_AutomationNameFormatWithoutPlaceholder_FallsBackToTheLetter()
        {
            (IReadOnlyList<LetterSection> top, _) =
                LetterSectionBuilder.Build(Tiles("Ahoi"), TitleOf, -1, FourPerRow, "Abschnitt");

            Assert.Equal("A", Assert.Single(top).AutomationName);
        }

        /// <summary>
        /// Die Fortsetzung unterhalb des Folgenbereichs behält den Namen ihres Abschnitts —
        /// sie gehört zu ihm, auch ohne sichtbare Überschrift.
        /// </summary>
        [Fact]
        public void Build_ContinuationKeepsTheAutomationNameOfItsSection()
        {
            (_, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.Build(
                    Tiles("Bibi", "Benjamin", "Bruno", "Bello", "Bonz"), TitleOf, 0, FourPerRow, "Abschnitt {0}");

            Assert.Equal("Abschnitt B", Assert.Single(bottom).AutomationName);
        }

        /// <summary>
        /// Listen, die nicht nach Namen sortiert sind, bekommen keine Buchstaben-Überschriften.
        /// Sie entstünden aus der Reihenfolge und behaupteten eine Ordnung, die es nicht gibt.
        /// </summary>
        [Fact]
        public void BuildUngrouped_WithoutSelection_YieldsOneSectionWithoutHeader()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.BuildUngrouped(Tiles("Bibi", "Ahoi", "TKKG"), -1, FourPerRow, "Serien");

            LetterSection only = Assert.Single(top);
            Assert.False(only.ShowsHeader);
            Assert.Equal("Serien", only.AutomationName);
            Assert.Equal(["Bibi", "Ahoi", "TKKG"], TitlesOf(only));
            Assert.Empty(bottom);
        }

        [Fact]
        public void BuildUngrouped_WithSelection_SplitsAtTheAccordion()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.BuildUngrouped(
                    Tiles("Eins", "Zwei", "Drei", "Vier", "Fünf"), 0, FourPerRow, "Serien");

            Assert.Equal(["Eins", "Zwei", "Drei", "Vier"], TitlesOf(Assert.Single(top)));
            Assert.Equal(["Fünf"], TitlesOf(Assert.Single(bottom)));
            Assert.All(top, s => Assert.False(s.ShowsHeader));
            Assert.All(bottom, s => Assert.False(s.ShowsHeader));
        }

        [Fact]
        public void BuildUngrouped_EmptyLibrary_YieldsNothingOnEitherSide()
        {
            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) =
                LetterSectionBuilder.BuildUngrouped(Tiles(), -1, FourPerRow, "Serien");

            Assert.Empty(top);
            Assert.Empty(bottom);
        }

        /// <summary>
        /// Ohne Namen liest die Sprachausgabe den Typnamen des Anzeigemodells vor — genau der
        /// Befund, der die Beschriftungspflicht ausgelöst hat.
        /// </summary>
        [Fact]
        public void BuildUngrouped_WithoutAutomationName_IsRejected()
        {
            _ = Assert.Throws<ArgumentException>(() =>
                LetterSectionBuilder.BuildUngrouped(Tiles("Ahoi"), -1, FourPerRow, "  "));
        }

        [Fact]
        public void Build_NullArguments_AreRejected()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                LetterSectionBuilder.Build<Tile>(null!, TitleOf, -1, FourPerRow));
            _ = Assert.Throws<ArgumentNullException>(() =>
                LetterSectionBuilder.Build(Tiles("Ahoi"), null!, -1, FourPerRow));
            _ = Assert.Throws<ArgumentNullException>(() =>
                LetterSectionBuilder.Build(Tiles("Ahoi"), TitleOf, -1, FourPerRow, null!));
        }
    }
}
