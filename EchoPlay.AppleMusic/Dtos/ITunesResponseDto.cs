using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EchoPlay.AppleMusic.Dtos
{
    /// <summary>
    /// Generischer Antwort-Container der iTunes Search/Lookup API.
    /// Alle Endpunkte liefern dasselbe Grundformat mit Ergebnisanzahl und Datenliste.
    /// </summary>
    /// <typeparam name="T">Typ der einzelnen Ergebnis-Elemente.</typeparam>
    public sealed class ITunesResponseDto<T>
    {
        /// <summary>
        /// Anzahl der zurückgegebenen Ergebnisse.
        /// </summary>
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; init; }

        /// <summary>
        /// Liste der Ergebnis-Elemente.
        /// Bei Lookup-Aufrufen kann das erste Element einen anderen Typ haben als die folgenden.
        /// </summary>
        [JsonPropertyName("results")]
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "DTO spiegelt iTunes-JSON; System.Text.Json deserialisiert direkt in List<T>.")]
        public List<T> Results { get; init; } = [];
    }
}
