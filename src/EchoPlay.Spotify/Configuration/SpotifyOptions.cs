namespace EchoPlay.Spotify.Configuration
{
    /// <summary>
    /// Enthält die konfigurierbaren Einstellungen für den Zugriff auf die Spotify-Web-API.
    /// Die Klasse ist ein reines Options-Modell und enthält keine Logik.
    /// </summary>
    public sealed class SpotifyOptions
    {
        /// <summary>
        /// Basisadresse der Spotify-Web-API.
        /// </summary>
        public Uri ApiBaseUrl { get; init; } = new("https://api.spotify.com/v1/");

        /// <summary>
        /// Basisadresse für die Spotify-Anmeldung.
        /// </summary>
        public Uri AuthBaseUrl { get; init; } = new("https://accounts.spotify.com/");

        /// <summary>
        /// ClientId der Spotify-App. Die Anwendung liest sie aus dem verschlüsselten Speicher des
        /// Nutzers, nie aus einer Konfigurationsdatei.
        /// </summary>
        public string ClientId { get; init; } = string.Empty;

        /// <summary>
        /// ClientSecret der Spotify-App. Die Anwendung liest es aus dem verschlüsselten Speicher des
        /// Nutzers, nie aus einer Konfigurationsdatei.
        /// </summary>
        public string ClientSecret { get; init; } = string.Empty;
    }
}
