using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Liest Dateien, die von außen kommen (Paket-Manifest, Mediathek-Manifest, Abgleichsdatei),
    /// nach derselben Regel: Eine leere oder übergroße Eingabe und kaputtes JSON werden mit einem
    /// Grund abgelehnt, bevor die Feldprüfung des jeweiligen Formats beginnt.
    /// </summary>
    public static class StrictJson
    {
        /// <summary>
        /// Deserialisiert <paramref name="json"/>, wenn es nicht leer und nicht länger als
        /// <paramref name="maxLength"/> ist.
        /// </summary>
        /// <typeparam name="T">Das Dokument des Formats.</typeparam>
        /// <param name="json">Der gelesene Inhalt.</param>
        /// <param name="maxLength">Die größte zulässige Länge in Zeichen.</param>
        /// <param name="options">Die Einstellungen des Formats.</param>
        /// <param name="emptyOrTooLarge">Der Grund, wenn die Eingabe leer oder zu groß ist.</param>
        /// <param name="invalidJson">Der Grund, wenn sie kein gültiges JSON ist.</param>
        /// <param name="document">Das Dokument; <see langword="null"/> auch bei gültigem JSON <c>null</c>.</param>
        /// <returns>Der Grund der Ablehnung, oder <see langword="null"/>, wenn gelesen wurde.</returns>
        public static string? TryDeserialize<T>(
            string? json,
            int maxLength,
            JsonSerializerOptions options,
            string emptyOrTooLarge,
            string invalidJson,
            [MaybeNull] out T document)
        {
            document = default;
            if (string.IsNullOrWhiteSpace(json) || json.Length > maxLength)
            {
                return emptyOrTooLarge;
            }

            try
            {
                document = JsonSerializer.Deserialize<T>(json, options);
                return null;
            }
            catch (JsonException)
            {
                return invalidJson;
            }
        }
    }
}
