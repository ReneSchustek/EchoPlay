using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// Liefert dem <see cref="SpotifyTokenClient"/> asynchron die aktuell gültigen
    /// Spotify-Client-Credentials. Erlaubt der App-Schicht, die Quelle (Credential-Store,
    /// Umgebungsvariable, Test-Double) zu wählen, ohne dass EchoPlay.Spotify die
    /// App-Implementierung kennen muss.
    /// </summary>
    public interface ISpotifyClientCredentialsProvider
    {
        /// <summary>
        /// Liefert ein Credential-Paar oder <see langword="null"/>, wenn der Nutzer
        /// keine Spotify-Zugangsdaten hinterlegt hat.
        /// </summary>
        Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default);
    }
}
