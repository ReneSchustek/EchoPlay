using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Repräsentiert eine einzelne Hörspielfolge innerhalb einer Serie.
    /// </summary>
    public sealed class Episode : BaseEntity
    {
        /// <summary>
        /// Titel der Episode.
        /// Beispiel: "Folge 87 – Der blaue Tod".
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Optionale Episodennummer.
        /// Kann bei manchen Anbietern fehlen oder nicht eindeutig sein.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public int? EpisodeNumber { get; set; }

        /// <summary>
        /// Gesamtdauer der Episode.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Veröffentlichungsdatum der Episode.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Fremdschlüssel zur zugehörigen Serie.
        /// </summary>
        [DataScope(DataScope.LocalIdentity)]
        public Guid SeriesId { get; set; }

        /// <summary>
        /// Zugehörige Hörspielserie.
        /// </summary>
        public Series Series { get; set; } = null!;

        /// <summary>
        /// Pfad zum lokalen Episodenordner auf dem Dateisystem.
        /// Nur gesetzt, wenn der Ordner dem Scanner-Muster entspricht und zugeordnet werden konnte.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string? LocalFolderPath { get; set; }

        /// <summary>
        /// Anzahl der lokalen Audiodateien im Episodenordner.
        /// Dient als Grundlage für den Abgleich mit der Online-Trackzahl.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public int? LocalTrackCount { get; set; }

        /// <summary>
        /// Ergebnis des Abgleichs zwischen lokalen und Online-Tracks.
        /// Standardmäßig <see cref="TrackMatchKind.NotMatched"/> bis ein Scan durchgeführt wurde.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public TrackMatchKind TrackMatchKind { get; set; } = TrackMatchKind.NotMatched;

        /// <summary>
        /// URL zum Öffnen der Folge beim Provider (Spotify/Apple Music).
        /// Wird beim Online-Import gesetzt. Null bei lokalen Folgen.
        /// Beispiel: "https://open.spotify.com/album/abc123"
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "TEXT-Spalte mit gewachsenem Bestand. Als Uri ließe eine einzige unlesbare Zeile beim Materialisieren die ganze Abfrage scheitern; wer die Adresse braucht, liest sie über WebAddress.")]
        [DataScope(DataScope.Shared)]
        public string? ProviderUrl { get; set; }

        /// <summary>
        /// URL zum Album-Cover beim Provider (Spotify/Apple Music).
        /// Wird beim Import gesetzt und dient als Fallback, wenn in der
        /// CoverImages-Tabelle noch kein Eintrag für die Episode existiert.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "TEXT-Spalte mit gewachsenem Bestand. Als Uri ließe eine einzige unlesbare Zeile beim Materialisieren die ganze Abfrage scheitern; wer die Adresse braucht, liest sie über WebAddress.")]
        [DataScope(DataScope.Cache)]
        public string? CoverImageUrl { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten automatischen Cover-Suche.
        /// Verhindert wiederholtes Durchsuchen bei Episoden, für die kein Cover gefunden wurde.
        /// Null bedeutet: noch nie geprüft. Nach einer erfolglosen Suche wird der Zeitpunkt
        /// gesetzt und erst nach Ablauf des Cooldowns (7 Tage) erneut gesucht.
        /// </summary>
        [DataScope(DataScope.Cache)]
        public DateTime? CoverLastChecked { get; set; }

        /// <summary>
        /// Album-ID der Episode bei Spotify.
        /// Wird gesetzt, wenn die Episode von Spotify importiert wurde.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string? SpotifyAlbumId { get; set; }

        /// <summary>
        /// Album-ID der Episode bei Apple Music.
        /// Wird gesetzt, wenn die Episode von Apple Music importiert wurde.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string? AppleMusicAlbumId { get; set; }
    }
}
