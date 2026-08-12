using EchoPlay.App.ViewModels;
using EchoPlay.Logger.Models;
using System;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die beiden Filterkriterien für sich — ohne ViewModel, ohne Liste, ohne
    /// Oberfläche. Was hier gilt, gilt in jeder Ansicht, die sie benutzt.
    /// </summary>
    public sealed class FilterCriteriaTests
    {
        private static LocalArtistCardViewModel Card(
            string title,
            bool isFavorite = false,
            bool isWatched = false,
            int local = 1,
            int total = 1) =>
            new(
                seriesId: Guid.NewGuid(),
                title: title,
                coverImage: null,
                localFolderPath: null,
                localEpisodeCount: local,
                totalEpisodeCount: total,
                isFavorite: isFavorite,
                isWatched: isWatched,
                scopeFactory: null!);

        private static LogEntryViewModel Entry(LogLevel level, string message, string category) =>
            new("12:00:00", level, category, message);

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
        /// Unvollständig heißt: Es sind Folgen bekannt, von denen welche fehlen. Ohne bekannte
        /// Folgen fehlt die Auskunft, nicht der Bestand.
        /// </summary>
        [Fact]
        public void ArtistFilter_IncompleteNeedsKnownEpisodes()
        {
            LocalArtistFilter filter = new() { IncompleteOnly = true };

            Assert.True(filter.Matches(Card("Lückenhaft", local: 1, total: 3)));
            Assert.False(filter.Matches(Card("Vollständig", local: 3, total: 3)));
            Assert.False(filter.Matches(Card("Ohne Folgen", local: 0, total: 0)));
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

        [Fact]
        public void LogFilter_WithoutCriteria_KeepsEverything()
        {
            LogEntryFilter filter = new();

            Assert.False(filter.IsActive);
            Assert.True(filter.Matches(Entry(LogLevel.Information, "Alles gut", "Start")));
        }

        /// <summary>
        /// Die Stufe filtert genau, nicht ab einer Schwelle — sonst brächte „Warnungen" auch
        /// jeden Fehler mit.
        /// </summary>
        [Fact]
        public void LogFilter_LevelMatchesExactlyAndNotAsThreshold()
        {
            LogEntryFilter filter = new() { Level = LogLevel.Warning };

            Assert.True(filter.Matches(Entry(LogLevel.Warning, "Bild fehlt", "Cover")));
            Assert.False(filter.Matches(Entry(LogLevel.Error, "Abbruch", "Datenbank")));
            Assert.False(filter.Matches(Entry(LogLevel.Information, "Bereit", "Start")));
        }

        [Fact]
        public void LogFilter_SearchCoversMessageAndCategory()
        {
            LogEntryFilter filter = new() { SearchText = "cover" };

            Assert.True(filter.Matches(Entry(LogLevel.Warning, "Bild fehlt", "Cover")));
            Assert.True(filter.Matches(Entry(LogLevel.Warning, "Cover nicht gefunden", "Bilder")));
            Assert.False(filter.Matches(Entry(LogLevel.Warning, "Abbruch", "Datenbank")));
        }

        [Fact]
        public void LogFilter_SearchAndLevelApplyTogether()
        {
            LogEntryFilter filter = new() { SearchText = "Abbruch", Level = LogLevel.Warning };

            Assert.False(filter.Matches(Entry(LogLevel.Error, "Abbruch", "Datenbank")));
            Assert.True(filter.Matches(Entry(LogLevel.Warning, "Abbruch", "Datenbank")));
        }

        [Fact]
        public void LogFilter_NullEntry_IsRejected()
        {
            LogEntryFilter filter = new();

            _ = Assert.Throws<ArgumentNullException>(() => filter.Matches(null!));
        }
    }
}
