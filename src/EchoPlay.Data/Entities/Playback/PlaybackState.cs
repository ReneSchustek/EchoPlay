using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;

namespace EchoPlay.Data.Entities.Playback
{
    /// <summary>
    /// Repräsentiert den persistierten Wiedergabestatus einer Episode.
    /// Diese Entität speichert dauerhaft relevante Informationen über Fortschritt, Abschluss und letzte Aktivität und dient als Grundlage
    /// für Resume-Funktionen, Fortschrittsanzeigen und Abschlusslogik.
    /// </summary>
    public class PlaybackState : BaseEntity
    {
        /// <summary>
        /// Fremdschlüssel zur zugehörigen Episode.
        /// Die Episode stellt den fachlichen Bezugspunkt für den Wiedergabestatus dar und definiert dessen Lebenszyklus.
        /// </summary>
        public Guid EpisodeId { get; set; }

        /// <summary>
        /// Navigation zur zugehörigen Episode.
        /// Diese Referenz erleichtert das Laden zusammenhängender Daten und wird nicht zur Steuerung von Cascades verwendet.
        /// </summary>
        public Episode Episode { get; set; } = null!;

        /// <summary>
        /// Letzte bekannte Abspielposition innerhalb der Episode.
        /// Dieser Wert ermöglicht das Fortsetzen der Wiedergabe an der zuletzt gehörten Stelle.
        /// </summary>
        public TimeSpan LastPosition { get; set; }

        /// <summary>
        /// Gibt an, ob die Episode vollständig gehört wurde.
        /// Dieses Flag dient als fachliche Abkürzung, um den Abschlusszustand ohne zusätzliche Berechnungen abfragen zu können.
        /// </summary>
        public bool IsCompleted { get; set; }

        /// <summary>
        /// Zeitpunkt, zu dem die Episode vollständig abgeschlossen wurde.
        /// Der Wert ist nur gesetzt, wenn <see cref="IsCompleted"/> wahr ist und dient unter anderem zur Sortierung und Statistikbildung.
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten Wiedergabeaktivität.
        /// Dieser Wert wird bei jeder Positionsänderung aktualisiert und erlaubt Aussagen darüber, wann eine Episode zuletzt  aktiv gehört wurde.
        /// </summary>
        public DateTime? LastPlayedAt { get; set; }

        /// <summary>
        /// Aktualisiert die Abspielposition und leitet daraus gegebenenfalls den Abschluss der Episode ab.
        /// Die Methode kapselt bewusst die fachliche Regel, ab wann eine Episode als vollständig gehört gilt,
        /// um diese Logik zentral und konsistent zu halten.
        /// </summary>
        /// <param name="position">Die neue Abspielposition innerhalb der Episode.</param>
        /// <param name="episodeDuration">Die Gesamtdauer der Episode.</param>
        public void UpdatePosition(TimeSpan position, TimeSpan episodeDuration)
        {
            LastPosition = position;
            LastPlayedAt = EntityClock.Current.UtcNow;

            if (!IsCompleted && position >= episodeDuration)
            {
                IsCompleted = true;
                CompletedAt = EntityClock.Current.UtcNow;
                LastPosition = episodeDuration;
            }
        }

        /// <summary>
        /// Markiert die Episode als vollständig gehört. Kapselt die kanonische Abschluss-Semantik
        /// (<see cref="IsCompleted"/>, <see cref="CompletedAt"/> und <see cref="LastPlayedAt"/> konsistent setzen),
        /// damit alle Wege zum Abschluss dieselbe Regel verwenden.
        /// </summary>
        /// <param name="completedAt">Der Abschlusszeitpunkt.</param>
        public void MarkCompleted(DateTime completedAt)
        {
            IsCompleted = true;
            CompletedAt = completedAt;
            LastPlayedAt = completedAt;
        }

        /// <summary>
        /// Setzt den Wiedergabestatus in den Ausgangszustand zurück.
        /// Diese Methode wird verwendet, wenn eine Episode bewusst erneut begonnen oder der Fortschritt verworfen werden soll.
        /// </summary>
        public void Reset()
        {
            LastPosition = TimeSpan.Zero;
            IsCompleted = false;
            CompletedAt = null;
            LastPlayedAt = null;
        }
    }
}
