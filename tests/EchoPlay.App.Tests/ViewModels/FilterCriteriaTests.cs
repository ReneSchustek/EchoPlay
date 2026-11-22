using EchoPlay.App.ViewModels;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Filterkriterium für sich, ohne ViewModel, ohne Liste, ohne
    /// Oberfläche. Was hier gilt, gilt in jeder Ansicht, die es benutzt.
    /// </summary>
    public sealed class FilterCriteriaTests
    {
        private static LocalArtistCardViewModel Card(
            string title,
            bool isFavorite = false,
            bool isWatched = false,
            int local = 1,
            int total = 1,
            bool isIncomplete = false) =>
            new(
                new LocalArtistCardData
                {
                    SeriesId = Guid.NewGuid(),
                    Title = title,
                    LocalEpisodeCount = local,
                    TotalEpisodeCount = total,
                    IsFavorite = isFavorite,
                    IsWatched = isWatched,
                    IsIncomplete = isIncomplete,
                },
                coverImage: null,
                scopeFactory: null!);

        [Fact]
        public void ArtistFilter_WithoutCriteria_KeepsEverything()
        {
            LocalArtistFilter filter = new();

            Assert.False(filter.IsActive);
            Assert.True(filter.Matches(Card("Bibi Blocksberg")));
        }

        [Fact]
        public void ArtistFilter_SearchIsCaseInsensitive()
        {
            LocalArtistFilter filter = new() { SearchText = "BIBI" };

            Assert.True(filter.Matches(Card("Bibi Blocksberg")));
            Assert.False(filter.Matches(Card("TKKG")));
        }

        /// <summary>
        /// Mehrere Schalter suchen die Schnittmenge: Wer „Favoriten" und „Überwacht" wählt,
        /// will beides zugleich.
        /// </summary>
        [Fact]
        public void ArtistFilter_SeveralSwitchesNarrowDownTogether()
        {
            LocalArtistFilter filter = new() { FavoritesOnly = true, WatchedOnly = true };

            Assert.False(filter.Matches(Card("Nur Favorit", isFavorite: true)));
            Assert.False(filter.Matches(Card("Nur überwacht", isWatched: true)));
            Assert.True(filter.Matches(Card("Beides", isFavorite: true, isWatched: true)));
        }

        /// <summary>
        /// Der Filter folgt der Angabe der Kachel, nicht den Folgenzählern: Eine lokale Serie
        /// kennt nur ihre eigenen Ordner, die Zähler sind dort immer gleich.
        /// </summary>
        [Fact]
        public void ArtistFilter_IncompleteFollowsTheCard()
        {
            LocalArtistFilter filter = new() { IncompleteOnly = true };

            Assert.True(filter.Matches(Card("Lückenhaft", isIncomplete: true)));
            Assert.False(filter.Matches(Card("Vollständig", local: 3, total: 3)));
            Assert.False(filter.Matches(Card("Zähler sagen lückenhaft", local: 1, total: 3)));
        }

        [Fact]
        public void ArtistFilter_Reset_ClearsEveryCriterion()
        {
            LocalArtistFilter filter = new()
            {
                SearchText = "Bibi",
                FavoritesOnly = true,
                WatchedOnly = true,
                IncompleteOnly = true
            };

            filter.Reset();

            Assert.False(filter.IsActive);
            Assert.Equal(string.Empty, filter.SearchText);
        }

        [Fact]
        public void ArtistFilter_NullCard_IsRejected()
        {
            LocalArtistFilter filter = new();

            _ = Assert.Throws<ArgumentNullException>(() => filter.Matches(null!));
        }

    }
}
