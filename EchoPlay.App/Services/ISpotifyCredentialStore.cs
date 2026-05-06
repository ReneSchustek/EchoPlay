using System.Threading;
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

        /// <summary>
        /// Wird auf <see langword="true"/> gesetzt, wenn <see cref="GetAsync"/> einen
        /// <see cref="System.Security.Cryptography.CryptographicException"/> abfängt und
        /// die korrupten Records selbständig löscht. Bleibt <see langword="true"/>, bis der
        /// Nutzer über das Settings-UI informiert wurde; die UI-Schicht setzt das Flag nach
        /// dem Anzeigen des Hinweises manuell zurück (Save/Clear setzen es ebenfalls zurück).
        /// </summary>
        bool LastLoadFailedDueToCorruption { get; }

        /// <summary>Setzt das Corruption-Flag zurück, nachdem der Nutzer informiert wurde.</summary>
        void AcknowledgeCorruptionNotice();

        /// <summary>Liest ClientId und ClientSecret aus dem Store. Null wenn keine vorhanden.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<(string ClientId, string ClientSecret)?> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>Verschlüsselt und speichert die Credentials.</summary>
        /// <param name="clientId">Spotify Client-ID.</param>
        /// <param name="clientSecret">Spotify Client-Secret.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SaveAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default);

        /// <summary>Löscht die gespeicherten Credentials.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task ClearAsync(CancellationToken cancellationToken = default);

        /// <summary>Initialisiert den In-Memory-Cache beim App-Start.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
