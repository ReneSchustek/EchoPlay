using EchoPlay.Data.Entities.Common;

namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Speichert verschlüsselte Einstellungswerte (z. B. Spotify-Credentials).
    /// Der Klartext wird nie in dieser Entity gehalten — die Verschlüsselung
    /// erfolgt in der App-Schicht (DPAPI), hier liegen nur die Cipher-Bytes.
    /// </summary>
    public sealed class SecureSetting : BaseEntity
    {
        /// <summary>Eindeutiger Schlüssel, z. B. "Spotify:ClientId".</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>DPAPI-verschlüsselter Wert als Byte-Array.</summary>
        public byte[] EncryptedValue { get; set; } = [];
    }
}
