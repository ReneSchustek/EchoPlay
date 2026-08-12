using EchoPlay.App.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Sichert die Buchstaben-Abschnitte der Mediathek ab.
    /// Der Kern: Genau der Abschnitt, in dem die gewählte Kachel steht, bekommt die
    /// Aufklappstelle — alle übrigen bleiben ungeteilt. Ohne das klappte das Akkordeon an
    /// einer anderen Stelle auf, als der Nutzer geklickt hat.
    /// </summary>
    public sealed class LetterGroupBuilderTests
    {
        /// <summary>Kachel-Ersatz für die Tests — nur ein Titel, mehr braucht die Gruppierung nicht.</summary>
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

        [Fact]
        public void Build_EmptyLibrary_YieldsNoGroups() =>
            Assert.Empty(LetterGroupBuilder.Build(Tiles(), TitleOf, -1, FourPerRow));

        [Fact]
        public void Build_GroupsConsecutiveTitlesByLetter()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "Benjamin", "TKKG");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, -1, FourPerRow);

            Assert.Equal(['A', 'B', 'T'], groups.Select(g => g.Letter));
            Assert.Equal(2, groups[1].Count);
        }

        /// <summary>
        /// Umlaute laufen auf ihren Grundbuchstaben — „Ärger" gehört in denselben Abschnitt
        /// wie „Ahoi" und nicht in ein eigenes Fach.
        /// </summary>
        [Fact]
        public void Build_DiacriticsShareTheBaseLetterGroup()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Ärger");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, -1, FourPerRow);

            LetterGroup<Tile> only = Assert.Single(groups);
            Assert.Equal('A', only.Letter);
            Assert.Equal(2, only.Count);
        }

        [Fact]
        public void Build_WithoutSelection_LeavesEveryGroupUnsplit()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "TKKG");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, -1, FourPerRow);

            Assert.All(groups, g => Assert.False(g.HoldsSelection));
            Assert.All(groups, g => Assert.Empty(g.BottomItems));
        }

        /// <summary>
        /// Der eigentliche Punkt des Umbaus: Nur der Abschnitt mit der Auswahl wird geteilt.
        /// </summary>
        [Fact]
        public void Build_SplitsOnlyTheGroupHoldingTheSelection()
        {
            // Auswahl steht in der B-Gruppe (Index 2 = "Bibi").
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Anton", "Bibi", "Benjamin", "TKKG");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, 2, FourPerRow);

            LetterGroup<Tile> a = groups.Single(g => g.Letter == 'A');
            LetterGroup<Tile> b = groups.Single(g => g.Letter == 'B');
            LetterGroup<Tile> t = groups.Single(g => g.Letter == 'T');

            Assert.False(a.HoldsSelection);
            Assert.True(b.HoldsSelection);
            Assert.False(t.HoldsSelection);

            // Die A-Gruppe bleibt vollständig oben, obwohl sie vor der Auswahl liegt.
            Assert.Equal(2, a.TopItems.Count);
            Assert.Empty(a.BottomItems);
        }

        /// <summary>
        /// Innerhalb der Gruppe klappt das Akkordeon unterhalb der Reihe auf, in der die
        /// gewählte Kachel steht — gerechnet mit dem gruppenlokalen Index, nicht mit dem
        /// Index über den Gesamtbestand.
        /// </summary>
        [Fact]
        public void Build_SplitUsesTheIndexInsideTheGroup()
        {
            // Fünf B-Titel bei vier Kacheln je Reihe: Auswahl auf dem ersten steht in Reihe 1,
            // die Aufklappstelle liegt damit nach vier Kacheln.
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "Benjamin", "Bruno", "Bello", "Bonz");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, 1, FourPerRow);
            LetterGroup<Tile> b = groups.Single(g => g.Letter == 'B');

            Assert.Equal(4, b.TopItems.Count);
            _ = Assert.Single(b.BottomItems);
            Assert.Equal("Bonz", b.BottomItems[0].Title);
        }

        [Fact]
        public void Build_KeepsTheGivenOrderInsideAGroup()
        {
            // Bewusst nicht alphabetisch innerhalb der Gruppe: Die Reihenfolge kommt aus der
            // Datenschicht, die Gruppierung darf sie nicht umsortieren.
            IReadOnlyList<Tile> tiles = Tiles("Bruno", "Bibi", "Benjamin");

            LetterGroup<Tile> b = Assert.Single(LetterGroupBuilder.Build(tiles, TitleOf, -1, FourPerRow));

            Assert.Equal(["Bruno", "Bibi", "Benjamin"], b.TopItems.Select(t => t.Title));
        }

        /// <summary>
        /// Ein Titel, der später erneut mit demselben Buchstaben auftaucht, gehört bei
        /// unsortierter Eingabe in einen zweiten Abschnitt — sonst risse die Anzeige aus der
        /// Reihenfolge, in der die Liste tatsächlich steht.
        /// </summary>
        [Fact]
        public void Build_UnsortedInput_YieldsSeparateGroupsInsteadOfReordering()
        {
            IReadOnlyList<Tile> tiles = Tiles("Bibi", "Ahoi", "Bruno");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, -1, FourPerRow);

            Assert.Equal(['B', 'A', 'B'], groups.Select(g => g.Letter));
        }

        [Fact]
        public void Build_SelectionIndexOutOfRange_IsTreatedAsNoSelection()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi");

            IReadOnlyList<LetterGroup<Tile>> groups = LetterGroupBuilder.Build(tiles, TitleOf, 99, FourPerRow);

            Assert.All(groups, g => Assert.False(g.HoldsSelection));
        }

        [Fact]
        public void OccupiedLetters_ReportsOnlyLettersInUse()
        {
            IReadOnlyList<Tile> tiles = Tiles("Ahoi", "Bibi", "5 Freunde");

            IReadOnlySet<char> occupied = LetterGroupBuilder.OccupiedLetters(tiles, TitleOf);

            Assert.Equal(['#', 'A', 'B'], occupied.OrderBy(c => c));
        }

        [Fact]
        public void Build_NullArguments_AreRejected()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                LetterGroupBuilder.Build<Tile>(null!, TitleOf, -1, FourPerRow));
            _ = Assert.Throws<ArgumentNullException>(() =>
                LetterGroupBuilder.Build(Tiles("A"), null!, -1, FourPerRow));
        }
    }
}
