using System.Net.Http;
using System.Text.Json;

namespace EchoPlay.Core.Http
{
    /// <summary>
    /// Erkennt erwartbare, tolerierbare Fehler beim Laden von Medien-Provider-Daten
    /// (Alben/Tracks über HTTP-APIs). Bei solchen Fehlern wird das betroffene Element
    /// übersprungen statt die Gesamtoperation abzubrechen.
    /// </summary>
    public static class TransientRequestError
    {
        /// <summary>
        /// Prüft, ob eine Ausnahme ein erwartbarer, überspringbarer Lade-/Netzwerkfehler ist.
        /// </summary>
        /// <param name="ex">Die aufgetretene Ausnahme.</param>
        /// <returns><c>true</c>, wenn der Fehler tolerierbar ist (Element überspringen).</returns>
        public static bool IsTransient(Exception ex) =>
            ex is HttpRequestException
               or TaskCanceledException
               or JsonException
               or InvalidOperationException
               or UriFormatException;
    }
}
