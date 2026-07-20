namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// Unveränderliches Credential-Paar für den Client-Credentials-Flow.
    /// </summary>
    /// <param name="ClientId">Spotify-App-ClientId.</param>
    /// <param name="ClientSecret">Zugehöriges ClientSecret.</param>
    public sealed record SpotifyClientCredentials(string ClientId, string ClientSecret);
}
