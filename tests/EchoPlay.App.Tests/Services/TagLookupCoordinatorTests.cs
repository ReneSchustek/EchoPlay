using EchoPlay.App.Services;
using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die internen pure-Funktion-Helper des <see cref="TagLookupCoordinator"/>.
    /// Wir testen den Core direkt — der HttpClient-Pfad waere ohne Live-API nicht sinnvoll
    /// abdeckbar; die SearchAsync-Funktion delegiert ohnehin an einen DI-aufgeloesten Service.
    /// </summary>
    public sealed class TagLookupCoordinatorTests
    {
        [Fact]
        public void BuildAutoLookupQueryCore_FullPath_ReturnsSeriesAndEpisode()
        {
            string folder = @"C:\Hoerspiele\Die drei ???\003 - Der Karpatenhund";

            string query = TagLookupCoordinator.BuildAutoLookupQueryCore(folder);

            Assert.Equal("Die drei ??? Der Karpatenhund", query);
        }

        [Fact]
        public void BuildAutoLookupQueryCore_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, TagLookupCoordinator.BuildAutoLookupQueryCore(null));
            Assert.Equal(string.Empty, TagLookupCoordinator.BuildAutoLookupQueryCore(string.Empty));
        }

        [Fact]
        public void SelectBestMatchCore_TrackCountExactMatch_PreferredOverFirst()
        {
            TagLookupResult first = new() { Title = "First-Hit", TrackCount = 5 };
            TagLookupResult exact = new() { Title = "Exact-Hit", TrackCount = 12 };
            List<TagLookupResult> results = [first, exact];

            TagLookupResult? selected = TagLookupCoordinator.SelectBestMatchCore(results, loadedTrackCount: 12);

            Assert.NotNull(selected);
            Assert.Equal("Exact-Hit", selected!.Title);
        }
    }
}
