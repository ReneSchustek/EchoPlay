using System.Text.RegularExpressions;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Baut den Weblink auf ein Apple-Music-Album. Windows reicht
    /// <c>https://music.apple.com/…</c> an die installierte Apple-Music-App weiter;
    /// ohne App öffnet die Webseite.
    /// </summary>
    /// <remarks>
    /// Die Storefront im Pfad ist Pflicht: <c>https://music.apple.com/album/&lt;id&gt;</c> antwortet
    /// mit 404, <c>…/de/album/&lt;id&gt;</c> mit 200 (am 2026-07-27 gegen die echte Adresse geprüft).
    /// Gleiche Linie wie <see cref="SpotifyAlbumLink"/>: Web-Link statt Custom-Protokoll, damit der
    /// http/https-Schutz in <see cref="SafeUrlLauncher"/> unangetastet bleibt.
    /// </remarks>
    internal static partial class AppleMusicAlbumLink
    {
        // Deutsche Storefront, passend zum Import (ProviderUrl steht ebenfalls auf /de/).
        private const string AlbumUrlPrefix = "https://music.apple.com/de/album/";

        /// <summary>
        /// Baut den Album-Link aus einer Apple-Music-Album-ID.
        /// </summary>
        /// <param name="appleMusicAlbumId">Die Album-ID (iTunes-CollectionId, rein numerisch).</param>
        /// <param name="url">Der fertige Link, oder <see langword="null"/> bei ungültiger ID.</param>
        /// <returns><see langword="true"/>, wenn ein Link gebaut werden konnte.</returns>
        public static bool TryBuild(string? appleMusicAlbumId, out string? url)
        {
            url = null;

            if (string.IsNullOrWhiteSpace(appleMusicAlbumId))
            {
                return false;
            }

            string id = appleMusicAlbumId.Trim();

            // Nur Ziffern: alles andere könnte den Pfad verlassen oder Query-Parameter anhängen.
            if (!AlbumIdPattern().IsMatch(id))
            {
                return false;
            }

            url = AlbumUrlPrefix + id;
            return true;
        }

        [GeneratedRegex("^[0-9]{4,15}$", RegexOptions.Compiled)]
        private static partial Regex AlbumIdPattern();
    }
}
