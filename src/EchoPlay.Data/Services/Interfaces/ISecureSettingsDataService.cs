using System;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Zugriff auf verschlüsselte Einstellungswerte in der SecureSettings-Tabelle.
    /// Arbeitet nur mit verschlüsselten Bytes — die Entschlüsselung erfolgt in der App-Schicht.
    /// </summary>
    public interface ISecureSettingsDataService
    {
        /// <summary>Liest den verschlüsselten Wert für den Schlüssel. Null wenn nicht vorhanden.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="key">Parameter key.</param>
        Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>Speichert oder aktualisiert den verschlüsselten Wert.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="key">Parameter key.</param>
        /// <param name="encryptedValue">Parameter encryptedValue.</param>
        Task SaveAsync(string key, byte[] encryptedValue, CancellationToken cancellationToken = default);

        /// <summary>Löscht den Eintrag für den Schlüssel.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="key">Parameter key.</param>
        Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    }
}
