using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using System;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Sichert ab, dass Such- und Filterkriterien den Seitenwechsel überstehen. Die
    /// Seiten-ViewModels sind kurzlebig; ohne diese Ablage stünde jede Liste nach einem
    /// Ausflug in eine Detailansicht wieder ungefiltert da.
    /// </summary>
    public sealed class FilterStateStoreTests
    {
        [Fact]
        public void GetOrCreate_ReturnsTheSameObjectForTheSamePage()
        {
            FilterStateStore store = new();

            LocalArtistFilter first = store.GetOrCreate<LocalArtistFilter>("LocalLibrary");
            first.SearchText = "Bibi";

            LocalArtistFilter second = store.GetOrCreate<LocalArtistFilter>("LocalLibrary");

            Assert.Same(first, second);
            Assert.Equal("Bibi", second.SearchText);
        }

        [Fact]
        public void GetOrCreate_KeepsPagesApart()
        {
            FilterStateStore store = new();

            store.GetOrCreate<LocalArtistFilter>("LocalLibrary").SearchText = "Bibi";
            LocalArtistFilter other = store.GetOrCreate<LocalArtistFilter>("OnlineLibrary");

            Assert.Equal(string.Empty, other.SearchText);
        }

        /// <summary>
        /// Zwei Seiten dürfen denselben Namen tragen und verschiedene Kriterien führen — sonst
        /// bekäme eine von beiden ein Objekt des falschen Typs.
        /// </summary>
        [Fact]
        public void GetOrCreate_KeepsDifferentCriteriaTypesApart()
        {
            FilterStateStore store = new();

            store.GetOrCreate<LocalArtistFilter>("Gemeinsam").SearchText = "Serien";
            LogEntryFilter messages = store.GetOrCreate<LogEntryFilter>("Gemeinsam");

            Assert.Equal(string.Empty, messages.SearchText);
        }

        [Fact]
        public void Clear_ForgetsEveryCriterion()
        {
            FilterStateStore store = new();
            store.GetOrCreate<LocalArtistFilter>("LocalLibrary").SearchText = "Bibi";

            store.Clear();

            Assert.Equal(string.Empty, store.GetOrCreate<LocalArtistFilter>("LocalLibrary").SearchText);
        }

        [Fact]
        public void GetOrCreate_WithoutPageKey_IsRejected()
        {
            FilterStateStore store = new();

            _ = Assert.Throws<ArgumentException>(() => store.GetOrCreate<LocalArtistFilter>("  "));
        }

        /// <summary>
        /// Der eigentliche Zweck: Ein neues ViewModel bekommt denselben Filter und zeigt
        /// deshalb dieselbe Sicht wie vor dem Seitenwechsel.
        /// </summary>
        [Fact]
        public void AFreshViewModel_TakesOverTheRememberedFilter()
        {
            FilterStateStore store = new();
            store.GetOrCreate<LocalArtistFilter>("LocalLibrary").FavoritesOnly = true;

            LocalArtistsViewModel neu = new(
                scopeFactory: null!,
                coverService: null,
                filter: store.GetOrCreate<LocalArtistFilter>("LocalLibrary"));

            Assert.True(neu.FavoritesOnly);
            Assert.True(neu.HasActiveFilter);
        }
    }
}
