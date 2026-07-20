using EchoPlay.Spotify.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Liefert zur Laufzeit die vollständigen <see cref="SpotifyOptions"/> inklusive
    /// Credentials aus dem Credential-Store. Gibt <see langword="null"/> zurück, wenn
    /// keine Credentials gesetzt sind — der Aufrufer muss dann den Spotify-Flow überspringen.
    /// </summary>

    public interface ISpotifyOptionsProvider
    {
        /// <summary>Ob Spotify-Credentials verfügbar sind (kein DB-Zugriff).</summary>
        bool IsAvailable { get; }

        /// <summary>Liest die vollständigen SpotifyOptions mit Credentials. Null wenn keine Credentials vorhanden.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<SpotifyOptions?> GetAsync(CancellationToken cancellationToken = default);
    }
}
