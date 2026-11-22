using System.Buffers;
using System.Security.Cryptography;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Kopiert einen Datenstrom und rechnet dabei die SHA-256 mit. Gemeinsamer Kern von Export,
    /// Einspielen und Mediathek-Archiv, damit überall dieselbe Grenze und dieselbe Prüfsumme gilt.
    /// </summary>
    internal static class HashingCopy
    {
        /// <summary>Puffergröße je Lesevorgang; groß genug für Laufwerke, klein genug für den Speicher.</summary>
        private const int BufferSize = 1 << 20;

        /// <summary>
        /// Kopiert höchstens <paramref name="maxLength"/> Byte und bricht ab, sobald mehr kommt. Die
        /// Länge in einem Archiv ist nur eine Angabe; gezählt wird deshalb selbst.
        /// </summary>
        /// <param name="source">Der Quellstrom.</param>
        /// <param name="target">Der Zielstrom.</param>
        /// <param name="maxLength">Höchstzahl der Byte.</param>
        /// <param name="exactLength">Ob es genau <paramref name="maxLength"/> Byte sein müssen.</param>
        /// <param name="onCopied">Erhält nach jedem Block die Zahl der neu kopierten Byte; darf fehlen.</param>
        /// <param name="cancellationToken">Bricht das Kopieren ab.</param>
        /// <returns>Die Zahl der kopierten Byte und die SHA-256 in Kleinbuchstaben.</returns>
        /// <exception cref="PackageRejectedException">Mehr als erlaubt, oder mit <paramref name="exactLength"/> weniger.</exception>
        public static async Task<(long Length, string Sha256)> CopyAsync(
            Stream source,
            Stream target,
            long maxLength,
            bool exactLength,
            Action<long>? onCopied,
            CancellationToken cancellationToken)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // Geliehen statt neu: Ein Puffer dieser Größe liegt im Large Object Heap, und beim Packen
            // der Mediathek entstünde je Datei einer; gemessen waren das 200 MB und Gen-2-Sammlungen
            // für 200 Dateien.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long total = 0;
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxLength)
                    {
                        throw new PackageRejectedException("Ein Eintrag ist länger als angegeben.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onCopied?.Invoke(read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (exactLength && total != maxLength)
            {
                throw new PackageRejectedException("Ein Eintrag ist kürzer als angegeben.");
            }

            return (total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
    }
}
