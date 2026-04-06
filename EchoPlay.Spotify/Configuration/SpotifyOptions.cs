namespace EchoPlay.Spotify.Configuration
{
    /// <summary>
    /// Enthält die konfigurierbaren Einstellungen für den Zugriff auf die Spotify-Web-API.
    /// Die Klasse ist ein reines Options-Modell und enthält bewusst keine Logik.
    /// </summary>
    public sealed class SpotifyOptions
    {
        /// <summary>
        /// Basis-URL der Spotify-Web-API.
        /// </summary>
        public string ApiBaseUrl { get; init; } = "https://api.spotify.com/v1/";

        /// <summary>
        /// Basis-URL für die Spotify-Authentifizierung.
        /// </summary>
        public string AuthBaseUrl { get; init; } = "https://accounts.spotify.com/";

        /// <summary>
        /// ClientId der Spotify-App.
        /// Muss in appsettings.Development.json oder per Umgebungsvariable gesetzt werden.
        /// </summary>
        public string ClientId { get; init; } = string.Empty;

        /// <summary>
        /// ClientSecret der Spotify-App.
        /// Muss in appsettings.Development.json oder per Umgebungsvariable gesetzt werden.
        /// </summary>
        public string ClientSecret { get; init; } = string.Empty;
    }
}
