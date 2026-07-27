using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die „Weiterhören"-Kachel. Der Anzeigetext kommt aus den Ressourcen —
    /// Singular und Plural sind getrennte Schlüssel, weil sich das Englische anders
    /// beugt als das Deutsche.
    /// </summary>
    public sealed class UnheardSeriesCardViewModelTests
    {
        [Fact]
        public void DisplayText_WithOneEpisode_UsesSingularResource()
        {
            FakeLocalizationService localization = new();

            UnheardSeriesCardViewModel card = new(
                TestIds.SeriesA, "TKKG", coverImage: null, unheardCount: 1, localization);

            Assert.Equal(localization.Get("UnheardEpisodesSingular"), card.DisplayText);
        }

        [Fact]
        public void DisplayText_WithSeveralEpisodes_FillsPluralResource()
        {
            FakeLocalizationService localization = new(new Dictionary<string, string>
            {
                ["UnheardEpisodesPlural"] = "{0} unplayed episodes"
            });

            UnheardSeriesCardViewModel card = new(
                TestIds.SeriesB, "TKKG", coverImage: null, unheardCount: 7, localization);

            Assert.Equal("7 unplayed episodes", card.DisplayText);
        }

        [Fact]
        public void DisplayText_WithoutLocalization_FallsBackToGerman()
        {
            // Ohne Lokalisierungsdienst (Unit-Test-Pfad) greift der eingebaute Text.
            UnheardSeriesCardViewModel card = new(
                TestIds.SeriesC, "TKKG", coverImage: null, unheardCount: 1);

            Assert.Equal("1 ungehörte Folge", card.DisplayText);
        }

        [Fact]
        public void Properties_AreTakenFromConstructor()
        {
            UnheardSeriesCardViewModel card = new(
                TestIds.SeriesD, "Die drei ???", coverImage: null, unheardCount: 3);

            Assert.Equal(TestIds.SeriesD, card.SeriesId);
            Assert.Equal("Die drei ???", card.SeriesName);
            Assert.Equal(3, card.UnheardCount);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public void OpenAutomationName_NamesActionAndSeries()
        {
            // Ohne diesen Namen liest ein Screenreader den ViewModel-Typnamen vor.
            UnheardSeriesCardViewModel card = new(
                TestIds.SeriesE, "Bibi Blocksberg", coverImage: null, unheardCount: 2);

            Assert.Contains("Bibi Blocksberg", card.OpenAutomationName, System.StringComparison.Ordinal);
        }
    }
}
