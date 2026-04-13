using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Speichert und liest Spotify-Credentials DPAPI-verschlüsselt aus der Datenbank.
    /// Der Hot-Path (<see cref="HasCredentials"/>) prüft nur einen In-Memory-Cache
    /// und verursacht keinen DB-Roundtrip.
    /// </summary>
    public interface ISpotifyCredentialStore
    {
        /// <summary>Ob Credentials im Store vorhanden sind (gecachter Wert, kein DB-Zugriff).</summary>
        bool HasCredentials { get; }

        /// <summary>Liest ClientId und ClientSecret aus dem Store. Null wenn keine vorhanden.</summary>
        Task<(string ClientId, string ClientSecret)?> GetAsync();

        /// <summary>Verschlüsselt und speichert die Credentials.</summary>
        Task SaveAsync(string clientId, string clientSecret);

        /// <summary>Löscht die gespeicherten Credentials.</summary>
        Task ClearAsync();

        /// <summary>Initialisiert den In-Memory-Cache beim App-Start.</summary>
        Task InitializeAsync();
    }
}
