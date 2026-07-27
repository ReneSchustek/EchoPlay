using EchoPlay.App.Helpers;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für <see cref="AccordionSplitHelper"/> – die Rechnung hinter dem Akkordeon
    /// der Mediathek. Sie bestimmt, nach welcher Kachelreihe der Detailbereich aufklappt.
    /// Stimmt sie nicht, öffnet sich das Akkordeon an der falschen Stelle.
    /// </summary>
    public sealed class AccordionSplitHelperTests
    {
        [Theory]
        [InlineData(1480.0, 10)]
        [InlineData(296.0, 2)]
        [InlineData(148.0, 1)]
        public void CalculateTilesPerRow_DividesBySlotWidth(double width, int expected)
        {
            Assert.Equal(expected, AccordionSplitHelper.CalculateTilesPerRow(width));
        }

        [Fact]
        public void CalculateTilesPerRow_TooNarrow_StillReturnsOne()
        {
            // Sonst käme im nächsten Schritt eine Division durch null.
            Assert.Equal(1, AccordionSplitHelper.CalculateTilesPerRow(10.0));
            Assert.Equal(1, AccordionSplitHelper.CalculateTilesPerRow(0.0));
        }

        [Fact]
        public void CalculateSplitIndex_FirstRow_SplitsAfterThatRow()
        {
            // Fünf Kacheln pro Reihe, Auswahl in der ersten Reihe → Schnitt nach Index 5.
            int split = AccordionSplitHelper.CalculateSplitIndex(
                selectedIndex: 2, totalCount: 20, availableWidth: 740.0);

            Assert.Equal(5, split);
        }

        [Fact]
        public void CalculateSplitIndex_SecondRow_SplitsAfterSecondRow()
        {
            int split = AccordionSplitHelper.CalculateSplitIndex(
                selectedIndex: 7, totalCount: 20, availableWidth: 740.0);

            Assert.Equal(10, split);
        }

        [Fact]
        public void CalculateSplitIndex_LastRow_IsCappedAtTotalCount()
        {
            // Die letzte Reihe ist meist nicht voll – der Schnitt darf nicht über das Ende gehen.
            int split = AccordionSplitHelper.CalculateSplitIndex(
                selectedIndex: 11, totalCount: 12, availableWidth: 740.0);

            Assert.Equal(12, split);
        }

        [Fact]
        public void Split_InTheMiddle_ReturnsBothParts()
        {
            IReadOnlyList<string> source = ["a", "b", "c", "d"];

            (IReadOnlyList<string> top, IReadOnlyList<string> bottom) =
                AccordionSplitHelper.Split(source, 2);

            Assert.Equal(["a", "b"], top);
            Assert.Equal(["c", "d"], bottom);
        }

        [Fact]
        public void Split_AtZero_PutsEverythingIntoBottom()
        {
            IReadOnlyList<string> source = ["a", "b"];

            (IReadOnlyList<string> top, IReadOnlyList<string> bottom) =
                AccordionSplitHelper.Split(source, 0);

            Assert.Empty(top);
            Assert.Equal(2, bottom.Count);
        }

        [Fact]
        public void Split_BeyondTheEnd_PutsEverythingIntoTop()
        {
            IReadOnlyList<string> source = ["a", "b"];

            (IReadOnlyList<string> top, IReadOnlyList<string> bottom) =
                AccordionSplitHelper.Split(source, 99);

            Assert.Equal(2, top.Count);
            Assert.Empty(bottom);
        }

        [Fact]
        public void Split_WithoutSource_Throws()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => AccordionSplitHelper.Split<string>(null!, 1));
        }
    }
}
