namespace EchoPlay.App.Services
{
    /// <summary>
    /// Welche Folge gestartet werden soll, und womit sie beim Anbieter zu finden ist, falls keine
    /// Datei vorliegt.
    /// </summary>
    /// <param name="EpisodeId">Die ID der Folge.</param>
    /// <param name="SeriesTitle">Serientitel, dient der Suche beim Anbieter, wenn keine Album-Kennung vorliegt.</param>
    /// <param name="EpisodeTitle">Folgentitel, ebenfalls für die Suche.</param>
    /// <param name="SpotifyAlbumId">Spotify-Album-Kennung, sofern der Aufrufer sie kennt.</param>
    /// <param name="AppleMusicAlbumId">Apple-Music-Album-Kennung, sofern der Aufrufer sie kennt.</param>
    public sealed record EpisodeLaunchRequest(
        Guid EpisodeId,
        string SeriesTitle,
        string EpisodeTitle,
        string? SpotifyAlbumId = null,
        string? AppleMusicAlbumId = null);
}
