using EchoPlay.App.ViewModels;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LocalArtistCardViewModel"/> – speziell das nachträgliche
    /// Aktualisieren der Episodenzähler, damit eine während des Scans mit noch 0 Episoden
    /// angelegte Kachel live auf die korrekte Zahl nachzieht statt auf "0 / 0" zu verharren.
    /// </summary>
    public sealed class LocalArtistCardViewModelTests
    {
        private static LocalArtistCardViewModel BuildCard(int local, int total) => new(
            seriesId: Guid.NewGuid(),
            title: "TKKG",
            coverImage: null,
            localFolderPath: @"D:\Mp3\Hörspiele\TKKG",
            localEpisodeCount: local,
            totalEpisodeCount: total,
            isFavorite: false,
            isWatched: false,
            scopeFactory: null!);

        [Fact]
        public void UpdateCounts_UpdatesValues_AndRaisesCountTextChanged()
        {
            LocalArtistCardViewModel card = BuildCard(0, 0);
            Assert.Equal("0 / 0", card.CountText);

            List<string?> changed = [];
            card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            card.UpdateCounts(localEpisodeCount: 7, totalEpisodeCount: 10);

            Assert.Equal(7, card.LocalEpisodeCount);
            Assert.Equal(10, card.TotalEpisodeCount);
            Assert.Equal("7 / 10", card.CountText);
            // Die gebundene Anzeige hängt an CountText – ohne diese Meldung bliebe die Kachel auf "0 / 0".
            Assert.Contains(nameof(LocalArtistCardViewModel.CountText), changed);
        }
    }
}
