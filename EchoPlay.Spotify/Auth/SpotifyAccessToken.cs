namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// Repräsentiert ein Zugriffstoken der Spotify-Web-API.
    /// Das Modell ist bewusst minimal gehalten und enthält nur Informationen, die für Ablaufsteuerung und Header-Setzung relevant sind.
    /// </summary>
    internal sealed class SpotifyAccessToken
    {
        /// <summary>
        /// Der eigentliche Zugriffstoken-Wert.
        /// </summary>
        public required string AccessToken { get; init; }

        /// <summary>
        /// Zeitpunkt, zu dem das Token abläuft.
        /// </summary>
        public DateTime ExpiresAtUtc { get; init; }

        /// <summary>
        /// Gibt an, ob das Token aktuell noch gültig ist.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    }
}