using EchoPlay.Data.Entities.Common;
using System.Diagnostics.CodeAnalysis;

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
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
            Justification = "EF Core speichert BLOBs als byte[]; Collection<byte> würde die EF-Mapping-Konvention brechen.")]
        public byte[] EncryptedValue { get; set; } = [];
    }
}
