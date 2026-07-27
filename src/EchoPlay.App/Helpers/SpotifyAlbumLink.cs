using System.Text.RegularExpressions;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Baut den Weblink auf ein Spotify-Album. Windows reicht <c>https://open.spotify.com/album/…</c>
    /// an die installierte Spotify-App weiter; ohne App öffnet die Webseite.
    /// </summary>
    /// <remarks>
    /// Bewusst der Web-Link und nicht das Protokoll <c>spotify:album:…</c>: <see cref="SafeUrlLauncher"/>
    /// lässt nur <c>http</c>/<c>https</c> durch, damit eine manipulierte Provider-Angabe aus der
    /// Datenbank kein beliebiges Programm starten kann. Der Web-Link erreicht dasselbe Ziel,
    /// ohne diesen Schutz aufzuweichen.
    /// </remarks>
    internal static partial class SpotifyAlbumLink
    {
        private const string AlbumUrlPrefix = "https://open.spotify.com/album/";

        /// <summary>
        /// Baut den Album-Link aus einer Spotify-Album-ID.
        /// </summary>
        /// <param name="spotifyAlbumId">Die Album-ID (Base62, 22 Zeichen).</param>
        /// <param name="url">Der fertige Link, oder <see langword="null"/> bei ungültiger ID.</param>
        /// <returns><see langword="true"/>, wenn ein Link gebaut werden konnte.</returns>
        public static bool TryBuild(string? spotifyAlbumId, out string? url)
        {
            url = null;

            if (string.IsNullOrWhiteSpace(spotifyAlbumId))
            {
                return false;
            }

            string id = spotifyAlbumId.Trim();

            // Gegen ID-Werte aus fremden Quellen absichern: alles außer Base62 könnte den
            // Pfad verlassen (../) oder Query-Parameter anhängen.
            if (!AlbumIdPattern().IsMatch(id))
            {
                return false;
            }

            url = AlbumUrlPrefix + id;
            return true;
        }

        [GeneratedRegex("^[A-Za-z0-9]{22}$", RegexOptions.Compiled)]
        private static partial Regex AlbumIdPattern();
    }
}
