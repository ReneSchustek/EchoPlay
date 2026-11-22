using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Eine Veröffentlichung im Kalender: ein Album des Anbieters, das im Vormonat, im
    /// laufenden Monat oder später erscheint, zu einer beliebigen vorhandenen Serie.
    /// </summary>
    /// <remarks>
    /// Eine eigene Tabelle neben <see cref="CachedNewRelease"/>, weil beide anderen Regeln
    /// folgen: Die Neuerscheinungen gelten nur für überwachte Serien und hängen am
    /// eingestellten Zeitfenster, der Kalender gilt für alle Serien und reicht stets bis
    /// zum Anfang des Vormonats zurück. In einer gemeinsamen Tabelle räumte jede Seite die
    /// Einträge der anderen weg.
    /// </remarks>
    public sealed class ReleaseCalendarEntry : BaseEntity, IProviderRelease
    {
        /// <summary>Kennung der Serie, zu der die Veröffentlichung gehört.</summary>
        [DataScope(DataScope.LocalIdentity)]
        public Guid SeriesId { get; set; }

        /// <summary>Die zugehörige Serie; liefert Name und Ersatz-Cover.</summary>
        public Series Series { get; set; } = null!;

        /// <summary>Albumname beim Anbieter, etwa „Folge 238: Der dunkle Taipan".</summary>
        [DataScope(DataScope.Cache)]
        public string Title { get; set; } = string.Empty;

        /// <summary>Aus dem Albumnamen gelesene Folgennummer; leer bei Sonderfolgen.</summary>
        [DataScope(DataScope.Cache)]
        public int? EpisodeNumber { get; set; }

        /// <summary>Erscheinungszeitpunkt beim Anbieter (UTC).</summary>
        [DataScope(DataScope.Cache)]
        public DateTime ReleaseDate { get; set; }

        /// <summary>Adresse des Album-Bilds in 100×100 Pixeln.</summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "TEXT-Spalte wie in CachedNewRelease. Als Uri ließe eine einzige unlesbare Zeile beim Materialisieren die ganze Abfrage scheitern.")]
        [DataScope(DataScope.Cache)]
        public string? CoverUrl { get; set; }

        /// <summary>Sammlungs-Kennung des Anbieters; fachlicher Schlüssel gegen Doppel.</summary>
        [DataScope(DataScope.Cache)]
        public long CollectionId { get; set; }

        /// <summary>Zeitpunkt der Prüfung, aus der der Eintrag stammt (UTC).</summary>
        [DataScope(DataScope.Cache)]
        public DateTime CheckedAtUtc { get; set; }

        /// <inheritdoc/>
        string? IProviderRelease.CoverAddress
        {
            get => CoverUrl;
            set => CoverUrl = value;
        }
    }
}
