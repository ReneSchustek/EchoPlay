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
        Task<byte[]?> GetAsync(string key);

        /// <summary>Speichert oder aktualisiert den verschlüsselten Wert.</summary>
        Task SaveAsync(string key, byte[] encryptedValue);

        /// <summary>Löscht den Eintrag für den Schlüssel.</summary>
        Task DeleteAsync(string key);
    }
}
