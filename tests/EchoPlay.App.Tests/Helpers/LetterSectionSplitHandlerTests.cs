using EchoPlay.App.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Sichert das Zusammenspiel ab, das die Mediathek-Seiten steuert: welche Abschnitte ober-
    /// und unterhalb des Folgenbereichs landen, wann nach Buchstaben gegliedert wird und wohin
    /// ein Sprung führt.
    /// </summary>
    public sealed class LetterSectionSplitHandlerTests
    {
        /// <summary>Kachel-Ersatz für die Tests.</summary>
        private sealed class Tile
        {
            public Tile(string title) => Title = title;
            public string Title { get; }
        }

        /// <summary>
        /// Anzeigestelle ohne Oberfläche: merkt sich die Abschnitte und protokolliert, welcher
        /// ins Blickfeld geholt wurde.
        /// </summary>
        private sealed class FakeHost : ILetterSectionHost
        {
            public IReadOnlyList<LetterSection> Sections { get; set; } = [];

            /// <summary>Der zuletzt angesprungene Abschnitt.</summary>
            public LetterSection? BroughtIntoView { get; private set; }

            /// <summary>Ob das Anspringen gelingt — bildet eine noch nicht gebaute Liste ab.</summary>
            public bool CanBringIntoView { get; set; } = true;

            public bool TryBringIntoView(LetterSection section)
            {
                if (!CanBringIntoView)
                {
                    return false;
                }

                BroughtIntoView = section;
                return true;
            }
        }

        private const double FourPerRow = 4 * AccordionSplitHelper.SeriesTileSlotWidth;

        private static IReadOnlyList<Tile> Tiles(params string[] titles) =>
            [.. titles.Select(t => new Tile(t))];

        /// <summary>
        /// Baut einen Handler über einer festen Kachelliste. <paramref name="groupByLetter"/>
        /// bildet die Sortierung der Seite nach.
        /// </summary>
        private static (LetterSectionSplitHandler<Tile> Handler, FakeHost Top, FakeHost Bottom) Build(
            IReadOnlyList<Tile> tiles,
            int selectedIndex = -1,
            Func<bool>? groupByLetter = null)
        {
            FakeHost top = new();
            FakeHost bottom = new();

            LetterSectionSplitHandler<Tile> handler = new(
                top,
                bottom,
                () => tiles,
                tile => tile.Title,
                () => selectedIndex,
                () => FourPerRow,
                "Abschnitt {0}",
                groupByLetter,
                groupByLetter is null ? null : "Serien");

            return (handler, top, bottom);
        }

        [Fact]
        public void UpdateSections_WithoutSelection_FillsOnlyTheUpperList()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, FakeHost bottom) =
                Build(Tiles("Ahoi", "Bibi", "TKKG"));

            handler.UpdateSections();

            Assert.Equal(["A", "B", "T"], top.Sections.Select(s => s.Header));
            Assert.Empty(bottom.Sections);
        }

        [Fact]
        public void UpdateSections_ReportsTheOccupiedLetters()
        {
            (LetterSectionSplitHandler<Tile> handler, _, _) = Build(Tiles("Ahoi", "Bibi", "5 Freunde"));

            handler.UpdateSections();

            Assert.Equal(['#', 'A', 'B'], handler.OccupiedLetters.OrderBy(c => c));
        }

        /// <summary>
        /// Ist die Liste nicht nach Namen sortiert, entfällt die Gliederung — sonst behauptete
        /// sie eine Ordnung, die es dort nicht gibt.
        /// </summary>
        [Fact]
        public void UpdateSections_WithoutAlphabeticalOrder_YieldsOneSectionWithoutHeader()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, _) =
                Build(Tiles("Bibi", "Ahoi", "TKKG"), groupByLetter: () => false);

            handler.UpdateSections();

            LetterSection only = Assert.Single(top.Sections);
            Assert.False(only.ShowsHeader);
            Assert.Equal("Serien", only.AutomationName);
            Assert.False(handler.IsGroupedByLetter);
        }

        [Fact]
        public void IsGroupedByLetter_WithoutASortingRule_IsAlwaysTrue()
        {
            (LetterSectionSplitHandler<Tile> handler, _, _) = Build(Tiles("Ahoi"));

            Assert.True(handler.IsGroupedByLetter);
        }

        /// <summary>
        /// Der Sprung sucht den Abschnitt, in dem der Buchstabe beginnt — und findet ihn auch,
        /// wenn er unterhalb des Folgenbereichs steht.
        /// </summary>
        [Fact]
        public void BringLetterIntoView_FindsTheSectionInTheLowerList()
        {
            // Auswahl auf "Ahoi": die Abschnitte danach wandern nach unten.
            (LetterSectionSplitHandler<Tile> handler, _, FakeHost bottom) =
                Build(Tiles("Ahoi", "Bibi", "TKKG"), selectedIndex: 0);
            handler.UpdateSections();

            Assert.True(handler.BringLetterIntoView('T'));
            Assert.Equal("T", bottom.BroughtIntoView?.Header);
        }

        [Fact]
        public void BringLetterIntoView_PrefersTheUpperList()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, _) =
                Build(Tiles("Ahoi", "Bibi", "TKKG"));
            handler.UpdateSections();

            Assert.True(handler.BringLetterIntoView('B'));
            Assert.Equal("B", top.BroughtIntoView?.Header);
        }

        /// <summary>
        /// Die Fortsetzung unterhalb des Folgenbereichs trägt denselben Buchstaben, ist aber
        /// nicht sein Anfang — ein Sprung dorthin landete mitten im Abschnitt.
        /// </summary>
        [Fact]
        public void BringLetterIntoView_SkipsTheContinuationWithoutHeader()
        {
            // Fünf B-Titel bei vier je Reihe: unten steht die Fortsetzung ohne Überschrift.
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, FakeHost bottom) =
                Build(Tiles("Bibi", "Benjamin", "Bruno", "Bello", "Bonz"), selectedIndex: 0);
            handler.UpdateSections();

            Assert.True(handler.BringLetterIntoView('B'));
            Assert.Equal("B", top.BroughtIntoView?.Header);
            Assert.Null(bottom.BroughtIntoView);
        }

        /// <summary>
        /// Nach dem Zuklappen des Folgenbereichs führt der Weg zurück zu der Serie, die offen
        /// war — nicht an den Anfang und nicht an eine zufällige Stelle.
        /// </summary>
        [Fact]
        public void BringItemIntoView_FindsTheSectionHoldingTheTile()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "TKKG");
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, _) = Build(tiles);
            handler.UpdateSections();

            Assert.True(handler.BringItemIntoView(tiles[2]));
            Assert.Equal("T", top.BroughtIntoView?.Header);
        }

        /// <summary>
        /// Auch wenn die Kachel unterhalb des Folgenbereichs steht — dort landet nach dem
        /// Aufklappen alles, was hinter der gewählten Serie kommt.
        /// </summary>
        [Fact]
        public void BringItemIntoView_AlsoLooksBelowTheAccordion()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "TKKG");
            (LetterSectionSplitHandler<Tile> handler, _, FakeHost bottom) = Build(tiles, selectedIndex: 0);
            handler.UpdateSections();

            Assert.True(handler.BringItemIntoView(tiles[2]));
            Assert.Equal("T", bottom.BroughtIntoView?.Header);
        }

        [Fact]
        public void BringItemIntoView_UnknownTile_ReportsFailure()
        {
            (LetterSectionSplitHandler<Tile> handler, _, _) = Build(Tiles("Ahoi"));
            handler.UpdateSections();

            Assert.False(handler.BringItemIntoView(new Tile("Nie geladen")));
        }

        [Fact]
        public void BringItemIntoView_NullTile_IsRejected()
        {
            (LetterSectionSplitHandler<Tile> handler, _, _) = Build(Tiles("Ahoi"));

            _ = Assert.Throws<ArgumentNullException>(() => handler.BringItemIntoView(null!));
        }

        [Fact]
        public void BringLetterIntoView_UnoccupiedLetter_ReportsFailure()
        {
            (LetterSectionSplitHandler<Tile> handler, _, _) = Build(Tiles("Ahoi", "Bibi"));
            handler.UpdateSections();

            Assert.False(handler.BringLetterIntoView('Z'));
        }

        /// <summary>
        /// Findet die Anzeigestelle den Abschnitt nicht — etwa weil die Liste noch nicht
        /// aufgebaut ist —, meldet der Handler das, statt einen Sprung vorzutäuschen.
        /// </summary>
        [Fact]
        public void BringLetterIntoView_WhenTheHostCannotReachIt_ReportsFailure()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, FakeHost bottom) = Build(Tiles("Ahoi"));
            handler.UpdateSections();
            top.CanBringIntoView = false;
            bottom.CanBringIntoView = false;

            Assert.False(handler.BringLetterIntoView('A'));
        }

        /// <summary>
        /// Die Größenänderung rechnet nur neu, wenn sich die Kachelzahl je Reihe ändert — sonst
        /// baute jede Pixelbewegung am Fensterrand die ganze Liste neu auf.
        /// </summary>
        [Fact]
        public void HandleSizeChanged_FirstCallBuildsTheSections()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, _) = Build(Tiles("Ahoi", "Bibi"));

            handler.HandleSizeChanged();

            Assert.Equal(2, top.Sections.Count);
        }

        [Fact]
        public void HandleSizeChanged_WithUnchangedRowWidth_DoesNotRebuild()
        {
            (LetterSectionSplitHandler<Tile> handler, FakeHost top, _) = Build(Tiles("Ahoi", "Bibi"));
            handler.HandleSizeChanged();

            IReadOnlyList<LetterSection> firstBuild = top.Sections;
            handler.HandleSizeChanged();

            Assert.Same(firstBuild, top.Sections);
        }

        /// <summary>
        /// Ohne Sortierregel braucht es keinen Namen für die ungegliederte Liste; mit Regel
        /// schon, sonst liest die Sprachausgabe den Typnamen vor.
        /// </summary>
        [Fact]
        public void Constructor_WithSortingRuleButNoName_IsRejected()
        {
            FakeHost host = new();

            _ = Assert.Throws<ArgumentException>(() => new LetterSectionSplitHandler<Tile>(
                host, host, () => [], t => t.Title, () => -1, () => FourPerRow,
                "Abschnitt {0}", () => true, "  "));
        }
    }
}
