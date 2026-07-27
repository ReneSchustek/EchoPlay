using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string SearchUrlPrefix = "https://open.spotify.com/search/";

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

        /// <summary>
        /// Baut einen Spotify-Suchlink für die Folge. Auffanglösung, wenn keine Album-ID
        /// vorliegt — etwa weil die Serie über Apple Music importiert wurde.
        /// </summary>
        /// <param name="searchTerms">Suchbegriffe, üblicherweise Serientitel und Folgentitel.</param>
        /// <param name="url">Der fertige Link, oder <see langword="null"/> ohne verwertbare Begriffe.</param>
        /// <returns><see langword="true"/>, wenn ein Link gebaut werden konnte.</returns>
        public static bool TrySearch(IEnumerable<string?> searchTerms, out string? url)
        {
            url = null;

            if (searchTerms is null)
            {
                return false;
            }

            string query = string.Join(' ', searchTerms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim()));

            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            // EscapeDataString kodiert auch '/' und '?' – die Begriffe können damit den Pfad
            // nicht verlassen, egal was in Serien- oder Folgentitel steht.
            url = SearchUrlPrefix + Uri.EscapeDataString(query);
            return true;
        }

        /// <summary>
        /// Baut die App-URI auf das Album (<c>spotify:album:&lt;id&gt;</c>).
        /// Öffnet die installierte Spotify-App, in der der Nutzer angemeldet ist.
        /// </summary>
        /// <param name="spotifyAlbumId">Die Album-ID (Base62, 22 Zeichen).</param>
        /// <param name="uri">Die fertige URI, oder <see langword="null"/> bei ungültiger ID.</param>
        /// <returns><see langword="true"/>, wenn eine URI gebaut werden konnte.</returns>
        public static bool TryBuildAppUri(string? spotifyAlbumId, out string? uri)
        {
            uri = null;

            if (string.IsNullOrWhiteSpace(spotifyAlbumId) || !AlbumIdPattern().IsMatch(spotifyAlbumId.Trim()))
            {
                return false;
            }

            uri = "spotify:album:" + spotifyAlbumId.Trim();
            return true;
        }

        /// <summary>
        /// Baut die App-URI für eine Suche (<c>spotify:search:&lt;begriffe&gt;</c>).
        /// </summary>
        /// <param name="searchTerms">Suchbegriffe, üblicherweise Serientitel und Folgentitel.</param>
        /// <param name="uri">Die fertige URI, oder <see langword="null"/> ohne verwertbare Begriffe.</param>
        /// <returns><see langword="true"/>, wenn eine URI gebaut werden konnte.</returns>
        public static bool TrySearchAppUri(IEnumerable<string?> searchTerms, out string? uri)
        {
            uri = null;

            if (!TrySearch(searchTerms, out string? webUrl) || webUrl is null)
            {
                return false;
            }

            // Denselben kodierten Suchteil wiederverwenden – er ist bereits escaped.
            uri = "spotify:search:" + webUrl[SearchUrlPrefix.Length..];
            return true;
        }

        [GeneratedRegex("^[A-Za-z0-9]{22}$", RegexOptions.Compiled)]
        private static partial Regex AlbumIdPattern();
    }
}
