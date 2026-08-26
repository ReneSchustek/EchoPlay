using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;

namespace EchoPlay.AppleMusic.Tests.Scoring
{
    /// <summary>
    /// Tests der Vorauswahl, die entscheidet, für welche Künstler sich die teure
    /// Albenprüfung überhaupt lohnt.
    /// </summary>
    /// <remarks>
    /// Die Suche nach „Bibi" lieferte 25 Künstler, darunter Musikkünstler aus aller Welt.
    /// Jeder von ihnen kostete vier Anfragen an einer Gegenstelle, die nur alle anderthalb
    /// Sekunden antwortet — zusammen über zwei Minuten für eine Suche.
    /// </remarks>
    public sealed class AppleMusicArtistPrefilterTests
    {
        private static readonly AppleMusicHoerspielSettings Settings = new();

        [Fact]
        public void SelectCandidates_WithoutNameOrGenreMatch_DropsTheArtist()
        {
            List<ITunesArtistDto> artists =
            [
                new ITunesArtistDto { ArtistId = 1, ArtistName = "Zabaione", PrimaryGenreName = "Pop" },
            ];

            AppleMusicPrefilterResult result = AppleMusicArtistPrefilter.SelectCandidates(artists, "Bibi", Settings);

            Assert.Empty(result.Candidates);
            Assert.Equal(1, result.Rejected);
        }

        [Fact]
        public void SelectCandidates_WithHoerspielGenreOnly_KeepsTheArtist()
        {
            // Der Name sagt nichts, das Genre schon — die Albenprüfung kann hier noch etwas finden.
            List<ITunesArtistDto> artists =
            [
                new ITunesArtistDto { ArtistId = 2, ArtistName = "Kiddinx", PrimaryGenreName = "Hörspiele" },
            ];

            AppleMusicPrefilterResult result = AppleMusicArtistPrefilter.SelectCandidates(artists, "Bibi", Settings);

            AppleMusicArtistCandidate candidate = Assert.Single(result.Candidates);
            Assert.Equal("Kiddinx", candidate.Artist.ArtistName);
            Assert.False(candidate.IsKnownSeries);
            Assert.Equal(0, result.Rejected);
        }

        [Fact]
        public void SelectCandidates_WithAKnownSeries_MarksItAndPutsItFirst()
        {
            List<ITunesArtistDto> artists =
            [
                new ITunesArtistDto { ArtistId = 3, ArtistName = "Bibi Babydoll", PrimaryGenreName = "Pop" },
                new ITunesArtistDto { ArtistId = 4, ArtistName = "Bibi Blocksberg", PrimaryGenreName = "Hörspiele" },
            ];

            AppleMusicPrefilterResult result = AppleMusicArtistPrefilter.SelectCandidates(artists, "Bibi", Settings);

            Assert.Equal(2, result.Candidates.Count);

            // Die bekannte Serie steht vorn und ist als solche erkannt — sie braucht keine
            // einzige weitere Anfrage, ihr Treffer steht sofort auf dem Schirm.
            Assert.Equal("Bibi Blocksberg", result.Candidates[0].Artist.ArtistName);
            Assert.True(result.Candidates[0].IsKnownSeries);
            Assert.False(result.Candidates[1].IsKnownSeries);
        }

        [Fact]
        public void SelectCandidates_WithAnExactWordMatch_RanksItAboveAPartialMatch()
        {
            List<ITunesArtistDto> artists =
            [
                new ITunesArtistDto { ArtistId = 5, ArtistName = "Bibiana Steinhaus", PrimaryGenreName = "Pop" },
                new ITunesArtistDto { ArtistId = 6, ArtistName = "Bibi und die Detektive", PrimaryGenreName = "Pop" },
            ];

            AppleMusicPrefilterResult result = AppleMusicArtistPrefilter.SelectCandidates(artists, "Bibi", Settings);

            Assert.Equal("Bibi und die Detektive", result.Candidates[0].Artist.ArtistName);
        }

        [Fact]
        public void SelectCandidates_WithAnEmptyResponse_ReturnsNothing()
        {
            AppleMusicPrefilterResult result = AppleMusicArtistPrefilter.SelectCandidates([], "Bibi", Settings);

            Assert.Empty(result.Candidates);
            Assert.Equal(0, result.Rejected);
        }
    }
}
