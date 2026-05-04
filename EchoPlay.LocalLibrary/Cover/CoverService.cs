using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Lädt und speichert Coverbilder für Hörspielserien und -episoden.
    /// </summary>
    public sealed class CoverService
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initialisiert den CoverService mit einem vorkonfigurierten HttpClient.
        /// </summary>
        /// <param name="httpClient">Der HttpClient für Download-Anfragen.</param>
        public CoverService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Lädt ein Coverbild von einer URL herunter.
        /// </summary>
        /// <param name="imageUrl">Die URL des Coverbilds.</param>
        /// <returns>Die Bilddaten als Byte-Array.</returns>
        /// <exception cref="HttpRequestException">
        /// Wird geworfen, wenn der Download fehlschlägt.
        /// </exception>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Internal API nimmt die URL so entgegen, wie sie in DB/DTO (Provider-URL-String) abgelegt ist. Uri-Refactor würde Cascade durch Cover-Kaskade erfordern und ist bewusst nicht umgesetzt.")]
        public async Task<byte[]> DownloadAsync(string imageUrl)
        {
            return await _httpClient.GetByteArrayAsync(new Uri(imageUrl, UriKind.Absolute)).ConfigureAwait(false);
        }

        /// <summary>
        /// Speichert Coverdaten als <c>cover.jpg</c> im angegebenen Ordner.
        /// Existiert die Datei bereits, wird sie nicht überschrieben.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum Zielordner.</param>
        /// <param name="data">Die zu speichernden Bilddaten.</param>
        public static async Task SaveToDirectoryAsync(string folderPath, byte[] data)
        {
            string targetPath = Path.Combine(folderPath, Core.CoverConstants.CoverFileName);

            // Vorhandene Cover nicht überschreiben – der Nutzer könnte sie manuell angepasst haben
            if (File.Exists(targetPath))
            {
                return;
            }

            await File.WriteAllBytesAsync(targetPath, data).ConfigureAwait(false);
        }
    }
}
