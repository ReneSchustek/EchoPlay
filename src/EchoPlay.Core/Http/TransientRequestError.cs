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

        /// <summary>
        /// Prüft wie <see cref="IsTransient(Exception)"/>, behandelt einen angeforderten
        /// Abbruch aber nicht mehr als tolerierbaren Fehler.
        /// </summary>
        /// <remarks>
        /// Ein abgebrochener HTTP-Aufruf meldet sich als <see cref="TaskCanceledException"/> —
        /// dieselbe Ausnahme, die auch eine Zeitüberschreitung erzeugt. Ohne den Blick auf das
        /// Abbruchzeichen sind beide nicht zu unterscheiden, und dann schreibt jeder
        /// abgebrochene Vorgang eine Warnung über einen Ausfall, den es nie gab. Die echten
        /// Ausfälle gehen darin unter.
        /// </remarks>
        /// <param name="ex">Die aufgetretene Ausnahme.</param>
        /// <param name="cancellationToken">Das Abbruchzeichen der umgebenden Operation.</param>
        /// <returns>
        /// <c>true</c>, wenn der Fehler tolerierbar ist. <c>false</c> bei einem angeforderten
        /// Abbruch — die Ausnahme gehört dann nach oben durchgereicht.
        /// </returns>
        public static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
            !(cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
            && IsTransient(ex);
    }
}
