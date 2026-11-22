using System.Globalization;
using EchoPlay.Core.Scoring;

namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Fachliche Schlüssel für Serien und Folgen: dieselbe Sache bekommt auf jedem Gerät denselben
    /// Schlüssel, auch wenn ihre Datenbank-Kennung dort eine andere ist.
    /// </summary>
    /// <remarks>
    /// Die Kennungen der Datenbank entstehen je Rechner. Importieren zwei Geräte dieselbe Serie,
    /// gibt es zwei Kennungen für eine Sache, und ein Abgleich über die Kennung legte sie doppelt an.
    /// Der Schlüssel nimmt deshalb, was außerhalb der Datenbank feststeht: die Kennung beim Anbieter,
    /// sonst den normalisierten Titel, für Folgen dazu die Nummer. Das ist dieselbe Zuordnung, die der
    /// Hörstatus schon zwischen lokaler und Online-Folge benutzt.
    /// <para>
    /// Lokale und Online-Serien gleichen Titels sind gewollt zwei Serien. Der Schlüssel trägt deshalb
    /// die Herkunft voran, sonst fielen sie im Abgleich zu einer zusammen.
    /// </para>
    /// </remarks>
    public static class BusinessKey
    {
        /// <summary>
        /// Der Schlüssel einer Serie.
        /// </summary>
        /// <param name="isOnlineImported">Ob die Serie aus einem Anbieter importiert wurde.</param>
        /// <param name="appleMusicArtistId">Kennung bei Apple Music, falls bekannt.</param>
        /// <param name="spotifyArtistId">Kennung bei Spotify, falls bekannt.</param>
        /// <param name="title">Der Titel der Serie.</param>
        /// <returns>Zum Beispiel <c>online:apple:12345</c> oder <c>local:title:</c> mit dem normalisierten Titel.</returns>
        public static string ForSeries(bool isOnlineImported, string? appleMusicArtistId, string? spotifyArtistId, string title)
        {
            ArgumentNullException.ThrowIfNull(title);

            if (!isOnlineImported)
            {
                // Lokal zählt der Ordnertitel; eine vom Folgencheck nachgetragene Anbieter-Kennung
                // gibt es nicht auf jedem Gerät und taugt deshalb nicht als Schlüssel.
                return "local:title:" + HoerspielTextNormalizer.Normalize(title);
            }

            if (!string.IsNullOrWhiteSpace(appleMusicArtistId))
            {
                return "online:apple:" + appleMusicArtistId.Trim();
            }

            return !string.IsNullOrWhiteSpace(spotifyArtistId)
                ? "online:spotify:" + spotifyArtistId.Trim()
                : "online:title:" + HoerspielTextNormalizer.Normalize(title);
        }

        /// <summary>
        /// Der Schlüssel einer Folge innerhalb ihrer Serie.
        /// </summary>
        /// <param name="seriesKey">Der Schlüssel der Serie aus <see cref="ForSeries"/>.</param>
        /// <param name="isOnlineImported">Ob die Serie aus einem Anbieter importiert wurde.</param>
        /// <param name="appleMusicAlbumId">Kennung des Albums bei Apple Music, falls bekannt.</param>
        /// <param name="spotifyAlbumId">Kennung des Albums bei Spotify, falls bekannt.</param>
        /// <param name="episodeNumber">Die Folgennummer, falls bekannt.</param>
        /// <param name="title">Der Titel der Folge.</param>
        /// <returns>Zum Beispiel <c>local:title:…#170</c>.</returns>
        public static string ForEpisode(
            string seriesKey,
            bool isOnlineImported,
            string? appleMusicAlbumId,
            string? spotifyAlbumId,
            int? episodeNumber,
            string title)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(seriesKey);
            ArgumentNullException.ThrowIfNull(title);

            // Bei Online-Serien ist das Album die Folge; die Nummer liest erst der Parser aus dem
            // Titel und kann auf zwei Geräten verschieden ausfallen.
            if (isOnlineImported)
            {
                if (!string.IsNullOrWhiteSpace(appleMusicAlbumId))
                {
                    return seriesKey + "|apple:" + appleMusicAlbumId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(spotifyAlbumId))
                {
                    return seriesKey + "|spotify:" + spotifyAlbumId.Trim();
                }
            }

            return episodeNumber is int number
                ? seriesKey + "#" + number.ToString(CultureInfo.InvariantCulture)
                : seriesKey + "|title:" + HoerspielTextNormalizer.Normalize(title);
        }
    }
}
