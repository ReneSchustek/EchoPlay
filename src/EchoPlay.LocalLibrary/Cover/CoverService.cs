using EchoPlay.Core.Security;

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
        /// Lädt ein Coverbild herunter.
        /// </summary>
        /// <param name="imageUrl">Die geprüfte Adresse des Coverbilds.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Bilddaten als Byte-Array.</returns>
        /// <exception cref="HttpRequestException">
        /// Wird geworfen, wenn der Download fehlschlägt.
        /// </exception>
        public async Task<byte[]> DownloadAsync(Uri imageUrl, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken).ConfigureAwait(false);
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

            // Der Dateiname ist eine Konstante; die Prüfung fängt eine künftige Änderung ab, die
            // einen Pfadanteil hineinbrächte. Sie vergleicht nur den Text, Verknüpfungen im
            // Dateisystem löst sie nicht auf.
            if (!SecurePathHelper.IsPathInside(targetPath, folderPath))
            {
                return;
            }

            // Eine vorhandene cover.jpg bleibt, sie kann von Hand ausgesucht sein.
            if (File.Exists(targetPath))
            {
                return;
            }

            await File.WriteAllBytesAsync(targetPath, data).ConfigureAwait(false);
        }
    }
}
