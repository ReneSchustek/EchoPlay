using EchoPlay.App.Models;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Sonderfolgen-Erkennung.
    /// Prüft, dass Episoden mit Nummer 0 oder null korrekt als Sonderfolgen markiert werden.
    /// </summary>
    public sealed class SpecialEpisodeTests
    {
        [Fact]
        public void LocalEpisodeCard_NumberZero_IsSpecial()
        {
            // Episodennummer 0 (z.B. "000 - Adventskalender") = Sonderfolge
            LocalEpisodeCardViewModel card = new(
                episodeId: TestIds.EpisodeA,
                episodeNumber: 0,
                title: "Adventskalender",
                localTrackCount: 3,
                folderPath: @"C:\Test",
                isSpecialEpisode: true);

            Assert.True(card.IsSpecialEpisode);
        }

        [Fact]
        public void LocalEpisodeCard_NumberNull_IsSpecial()
        {
            // Keine Episodennummer (kein Nummernmuster erkannt) = Sonderfolge
            LocalEpisodeCardViewModel card = new(
                episodeId: TestIds.EpisodeB,
                episodeNumber: null,
                title: "Bonusmaterial",
                localTrackCount: 1,
                folderPath: @"C:\Test",
                isSpecialEpisode: true);

            Assert.True(card.IsSpecialEpisode);
        }

        [Fact]
        public void LocalEpisodeCard_RegularNumber_NotSpecial()
        {
            // Reguläre Episodennummer > 0 = keine Sonderfolge
            LocalEpisodeCardViewModel card = new(
                episodeId: TestIds.EpisodeC,
                episodeNumber: 42,
                title: "Der Super-Papagei",
                localTrackCount: 5,
                folderPath: @"C:\Test",
                isSpecialEpisode: false);

            Assert.False(card.IsSpecialEpisode);
        }

        [Fact]
        public void EpisodeTile_NumberZero_IsSpecial()
        {
            EpisodeTileViewModel tile = new(
                episodeId: TestIds.EpisodeD,
                episodeNumber: 0,
                title: "Sonderfolge",
                totalDuration: null,
                playbackStatus: PlaybackStatus.NotStarted,
                releaseDate: null,
                playEpisode: () => { },
                isSpecialEpisode: true);

            Assert.True(tile.IsSpecialEpisode);
        }

        [Fact]
        public void EpisodeTile_RegularNumber_NotSpecial()
        {
            EpisodeTileViewModel tile = new(
                episodeId: TestIds.EpisodeE,
                episodeNumber: 229,
                title: "Drehbuch der Täuschung",
                totalDuration: null,
                playbackStatus: PlaybackStatus.NotStarted,
                releaseDate: null,
                playEpisode: () => { },
                isSpecialEpisode: false);

            Assert.False(tile.IsSpecialEpisode);
        }
    }
}
