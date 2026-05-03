using EchoPlay.Spotify.Auth;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISpotifyClientCredentialsProvider"/>.
    /// Gibt entweder ein konfiguriertes Credentials-Objekt oder <see langword="null"/> zurück —
    /// passend für Tests, die den Spotify-Fallback-Pfad im <c>ImportService</c> abdecken.
    /// </summary>
    internal sealed class FakeSpotifyClientCredentialsProvider : ISpotifyClientCredentialsProvider
    {
        private readonly SpotifyClientCredentials? _credentials;

        /// <summary>Erstellt den Fake mit festgelegter Antwort für <see cref="GetAsync"/>.</summary>
        /// <param name="credentials">Die zu liefernden Credentials oder <see langword="null"/>.</param>
        public FakeSpotifyClientCredentialsProvider(SpotifyClientCredentials? credentials)
        {
            _credentials = credentials;
        }

        /// <summary>Factory für den Standardfall „Credentials vorhanden" mit Dummy-Werten.</summary>
        public static FakeSpotifyClientCredentialsProvider WithCredentials()
            => new(new SpotifyClientCredentials("test-client-id", "test-client-secret"));

        /// <summary>Factory für den Fallback-Fall „keine Credentials hinterlegt".</summary>
        public static FakeSpotifyClientCredentialsProvider Missing()
            => new(credentials: null);

        /// <inheritdoc/>
        public Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_credentials);
    }
}
