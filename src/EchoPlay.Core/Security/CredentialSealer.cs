using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace EchoPlay.Core.Security
{
    /// <summary>
    /// Verschlüsselt Zugangsdaten mit einem Kennwort, damit sie in einem Paket den Rechner verlassen
    /// können.
    /// </summary>
    /// <remarks>
    /// Auf dem Rechner liegen die Zugangsdaten per DPAPI verschlüsselt, gebunden an Benutzer und
    /// Rechner; auf einem anderen Rechner sind sie damit wertlos. Für den Weg dazwischen verschlüsselt
    /// diese Klasse mit AES-GCM, der Schlüssel wird mit PBKDF2 (SHA-256, Salz, 600.000 Durchläufe) aus
    /// dem Kennwort abgeleitet. Beides bringt .NET mit, ein weiteres Paket braucht es nicht. GCM prüft
    /// beim Entschlüsseln, ob Kennwort und Inhalt stimmen; ein falsches Kennwort und ein veränderter
    /// Inhalt sehen gleich aus und werden gleich abgelehnt.
    /// <para>
    /// Aufbau: <c>EPS1</c>, Durchläufe (Int32, little endian), Salz (16), Nonce (12), Tag (16),
    /// verschlüsselter Inhalt. Die Durchläufe stehen im Paket, damit sie später steigen können; beim
    /// Öffnen gilt eine Ober- und Untergrenze, sonst könnte ein präpariertes Paket die Anwendung mit
    /// einer Milliarde Durchläufe lahmlegen oder mit zehn das Kennwort schutzlos machen.
    /// </para>
    /// </remarks>
    public static class CredentialSealer
    {
        /// <summary>Durchläufe der Schlüsselableitung beim Verschlüsseln.</summary>
        public const int Iterations = 600_000;

        private const int MinIterations = 100_000;
        private const int MaxIterations = 5_000_000;
        private const int SaltLength = 16;
        private const int NonceLength = 12;
        private const int TagLength = 16;
        private const int KeyLength = 32;
        private static readonly byte[] Magic = "EPS1"u8.ToArray();
        private static readonly int HeaderLength = Magic.Length + sizeof(int) + SaltLength + NonceLength + TagLength;

        /// <summary>
        /// Verschlüsselt einen Inhalt mit einem Kennwort.
        /// </summary>
        /// <param name="plaintext">Der Klartext.</param>
        /// <param name="password">Das Kennwort, nicht leer.</param>
        /// <returns>Der verschlüsselte Inhalt im beschriebenen Aufbau.</returns>
        public static byte[] Seal(ReadOnlySpan<byte> plaintext, string password)
        {
            ArgumentException.ThrowIfNullOrEmpty(password);

            byte[] sealedData = new byte[HeaderLength + plaintext.Length];
            Span<byte> span = sealedData;
            Magic.CopyTo(span);
            BinaryPrimitives.WriteInt32LittleEndian(span[Magic.Length..], Iterations);
            Span<byte> salt = span.Slice(Magic.Length + sizeof(int), SaltLength);
            Span<byte> nonce = span.Slice(Magic.Length + sizeof(int) + SaltLength, NonceLength);
            Span<byte> tag = span.Slice(Magic.Length + sizeof(int) + SaltLength + NonceLength, TagLength);
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(nonce);

            byte[] key = DeriveKey(password, salt, Iterations);
            try
            {
                using AesGcm gcm = new(key, TagLength);
                gcm.Encrypt(nonce, plaintext, span[HeaderLength..], tag);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            return sealedData;
        }

        /// <summary>
        /// Entschlüsselt einen Inhalt aus <see cref="Seal"/>.
        /// </summary>
        /// <param name="sealedData">Der verschlüsselte Inhalt.</param>
        /// <param name="password">Das Kennwort.</param>
        /// <param name="plaintext">Der Klartext; der Aufrufer löscht ihn nach Gebrauch.</param>
        /// <returns>
        /// <see langword="false"/> bei falschem Kennwort, verändertem Inhalt oder fremdem Aufbau.
        /// </returns>
        public static bool TryOpen(ReadOnlySpan<byte> sealedData, string password, out byte[] plaintext)
        {
            plaintext = [];
            if (string.IsNullOrEmpty(password) || !TryReadIterations(sealedData, out int iterations))
            {
                return false;
            }

            ReadOnlySpan<byte> salt = sealedData.Slice(Magic.Length + sizeof(int), SaltLength);
            ReadOnlySpan<byte> nonce = sealedData.Slice(Magic.Length + sizeof(int) + SaltLength, NonceLength);
            ReadOnlySpan<byte> tag = sealedData.Slice(Magic.Length + sizeof(int) + SaltLength + NonceLength, TagLength);
            ReadOnlySpan<byte> cipher = sealedData[HeaderLength..];

            byte[] key = DeriveKey(password, salt, iterations);
            byte[] result = new byte[cipher.Length];
            try
            {
                using AesGcm gcm = new(key, TagLength);
                gcm.Decrypt(nonce, cipher, tag, result);
                plaintext = result;
                return true;
            }
            catch (AuthenticationTagMismatchException)
            {
                CryptographicOperations.ZeroMemory(result);
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>
        /// Prüft Länge und Kennung des Kopfs und liest die Zahl der Ableitungsrunden; eine Zahl
        /// außerhalb der Grenzen gilt als fremder Aufbau.
        /// </summary>
        private static bool TryReadIterations(ReadOnlySpan<byte> sealedData, out int iterations)
        {
            iterations = 0;
            if (sealedData.Length < HeaderLength || !sealedData[..Magic.Length].SequenceEqual(Magic))
            {
                return false;
            }

            iterations = BinaryPrimitives.ReadInt32LittleEndian(sealedData[Magic.Length..]);
            return iterations is >= MinIterations and <= MaxIterations;
        }

        private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, KeyLength);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
    }
}
