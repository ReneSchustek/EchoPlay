using EchoPlay.Data.Entities.Common;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Zwischengespeichertes iTunes-Ergebnis für eine Neuerscheinung.
    /// Wird beim ersten iTunes-Check pro Serie angelegt und bei Folge-Checks aktualisiert.
    /// Dadurch kann das Dashboard gespeicherte Neuerscheinungen sofort anzeigen,
    /// ohne auf die langsame iTunes-API warten zu müssen.
    /// </summary>
    /// <remarks>
    /// Jeder Eintrag entspricht einem iTunes-Album, das im konfigurierten Zeitfenster
    /// erschienen ist (oder angekündigt wurde). Die <see cref="CollectionId"/> ist der
    /// eindeutige Schlüssel aus der iTunes-API – damit werden Duplikate beim erneuten
    /// Abruf verhindert.
    /// </remarks>
    public class CachedNewRelease : BaseEntity
    {
        /// <summary>
        /// ID der Serie, zu der diese Neuerscheinung gehört.
        /// Fremdschlüssel auf <see cref="Series"/>.
        /// </summary>
        public Guid SeriesId { get; set; }

        /// <summary>
        /// Navigation zur zugehörigen Serie – wird für Dashboard-Kacheln benötigt
        /// (Serienname, Cover-Fallback).
        /// </summary>
        public Series Series { get; set; } = null!;

        /// <summary>
        /// Albumname aus iTunes (z.B. "Die drei ??? - Folge 238 - Der dunkle Taipan").
        /// Wird als Episodentitel auf der Kachel angezeigt.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Extrahierte Folgennummer aus dem Albumnamen (z.B. 238).
        /// Null wenn keine Nummer erkannt wurde (Sonderfolgen, Compilations).
        /// Wird für den Abgleich mit lokalen Episoden verwendet.
        /// </summary>
        public int? EpisodeNumber { get; set; }

        /// <summary>
        /// Veröffentlichungsdatum aus iTunes (UTC).
        /// Liegt das Datum in der Zukunft, handelt es sich um eine Ankündigung.
        /// </summary>
        public DateTime ReleaseDate { get; set; }

        /// <summary>
        /// URL zum Album-Cover (100×100 Pixel).
        /// Kann über das iTunes-URL-Pattern auf größere Auflösungen skaliert werden
        /// (z.B. /100x100bb → /600x600bb).
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Entity spiegelt DB-Spalte CoverUrl (iTunes-Artwork-URL als TEXT); Uri-Umwandlung würde EF-Core-Mapping und iTunes-URL-Pattern-Manipulation (100x100bb → 600x600bb) komplizieren.")]
        public string? CoverUrl { get; set; }

        /// <summary>
        /// iTunes-Collection-ID – eindeutiger Schlüssel für dieses Album in der iTunes-API.
        /// Wird als fachlicher Unique-Key verwendet, damit ein Album nicht mehrfach
        /// im Cache landet.
        /// </summary>
        public long CollectionId { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten iTunes-Prüfung für diesen Eintrag (UTC).
        /// Anhand dieses Werts entscheidet der Hintergrund-Updater, ob die Serie
        /// erneut bei iTunes abgefragt werden muss.
        /// </summary>
        public DateTime CheckedAtUtc { get; set; }
    }
}
