using EchoPlay.Core.Http;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EchoPlay.AppleMusic.Mapping
{
    /// <summary>
    /// Hebt die Auflösung einer iTunes-Cover-Adresse auf die gewünschte Kantenlänge.
    /// </summary>
    /// <remarks>
    /// Die Suchantwort liefert das Artwork nur in 100 Pixeln (<c>artworkUrl100</c>). Die
    /// Trefferkachel ist 160 Pixel breit und wird auf hochauflösenden Schirmen skaliert;
    /// ein 100er-Bild sieht dort ausgefranst aus. Die Größe steht im letzten Pfadstück der
    /// Adresse (<c>…/100x100bb.jpg</c>) und lässt sich austauschen, ohne eine weitere
    /// Anfrage zu stellen.
    /// </remarks>
    public static partial class AppleMusicArtworkUrl
    {
        /// <summary>Kantenlänge, die für die Trefferkachel angefordert wird.</summary>
        public const int SearchResultTileSize = 300;

        /// <summary>
        /// Kantenlänge für das Cover einer Folge. Es wird gespeichert und auch groß in der
        /// Detailansicht gezeigt.
        /// </summary>
        public const int EpisodeCoverSize = 600;

        [GeneratedRegex(@"/\d+x\d+(?<suffix>[a-z]*)\.(?<extension>jpg|png)$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex SizeSegmentPattern();

        /// <summary>
        /// Ersetzt die Größenangabe im Pfad durch <paramref name="size"/>.
        /// </summary>
        /// <param name="artworkUrl">Die geprüfte Adresse aus der Anbieter-Antwort.</param>
        /// <param name="size">Die gewünschte Kantenlänge in Pixeln.</param>
        /// <returns>
        /// Die angepasste Adresse. Trägt die Adresse keine erkennbare Größe, kommt sie
        /// unverändert zurück; eine geratene Umschrift wäre schlechter als das kleine Bild.
        /// Bei <see langword="null"/> bleibt es bei <see langword="null"/>.
        /// </returns>
        public static Uri? WithSize(Uri? artworkUrl, int size = SearchResultTileSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(size, 0);

            if (artworkUrl is null)
            {
                return null;
            }

            string edge = size.ToString(CultureInfo.InvariantCulture);
            string resized = SizeSegmentPattern().Replace(
                artworkUrl.OriginalString,
                match => $"/{edge}x{edge}{match.Groups["suffix"].Value}.{match.Groups["extension"].Value}");

            // Die Umschrift tauscht nur Ziffern im letzten Pfadstück; das Ergebnis ist deshalb
            // immer wieder eine gültige Adresse.
            return WebAddress.TryParse(resized) ?? artworkUrl;
        }
    }
}
