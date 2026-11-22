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
                new LocalEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeA,
                    Title = "Adventskalender",
                    LocalTrackCount = 3,
                    FolderPath = @"C:\Test",
                    IsSpecialEpisode = true,
                });

            Assert.True(card.IsSpecialEpisode);
        }

        [Fact]
        public void LocalEpisodeCard_NumberNull_IsSpecial()
        {
            // Keine Episodennummer (kein Nummernmuster erkannt) = Sonderfolge
            LocalEpisodeCardViewModel card = new(
                new LocalEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeB,
                    Title = "Bonusmaterial",
                    LocalTrackCount = 1,
                    FolderPath = @"C:\Test",
                    IsSpecialEpisode = true,
                });

            Assert.True(card.IsSpecialEpisode);
        }

        [Fact]
        public void LocalEpisodeCard_RegularNumber_NotSpecial()
        {
            // Reguläre Episodennummer > 0 = keine Sonderfolge
            LocalEpisodeCardViewModel card = new(
                new LocalEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeC,
                    EpisodeNumber = 42,
                    Title = "Der Super-Papagei",
                    LocalTrackCount = 5,
                    FolderPath = @"C:\Test",
                });

            Assert.False(card.IsSpecialEpisode);
        }

        [Fact]
        public void EpisodeTile_NumberZero_IsSpecial()
        {
            EpisodeTileViewModel tile = new(
                new EpisodeTileData { EpisodeId = TestIds.EpisodeD, EpisodeNumber = 0, Title = "Sonderfolge", IsSpecialEpisode = true },
                playEpisode: () => { });

            Assert.True(tile.IsSpecialEpisode);
        }

        [Fact]
        public void EpisodeTile_RegularNumber_NotSpecial()
        {
            EpisodeTileViewModel tile = new(
                new EpisodeTileData { EpisodeId = TestIds.EpisodeE, EpisodeNumber = 229, Title = "Drehbuch der Täuschung" },
                playEpisode: () => { });

            Assert.False(tile.IsSpecialEpisode);
        }
    }
}
