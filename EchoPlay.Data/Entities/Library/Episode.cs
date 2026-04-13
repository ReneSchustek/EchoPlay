using EchoPlay.Data.Entities.Common;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Repräsentiert eine einzelne Hörspielfolge innerhalb einer Serie.
    /// </summary>
    public class Episode : BaseEntity
    {
        /// <summary>
        /// Titel der Episode.
        /// Beispiel: "Folge 87 – Der blaue Tod".
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Optionale Episodennummer.
        /// Kann bei manchen Anbietern fehlen oder nicht eindeutig sein.
        /// </summary>
        public int? EpisodeNumber { get; set; }

        /// <summary>
        /// Gesamtdauer der Episode.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Veröffentlichungsdatum der Episode.
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Fremdschlüssel zur zugehörigen Serie.
        /// </summary>
        public Guid SeriesId { get; set; }

        /// <summary>
        /// Zugehörige Hörspielserie.
        /// </summary>
        public Series Series { get; set; } = null!;

        /// <summary>
        /// Pfad zum lokalen Episodenordner auf dem Dateisystem.
        /// Nur gesetzt, wenn der Ordner dem Scanner-Muster entspricht und zugeordnet werden konnte.
        /// </summary>
        public string? LocalFolderPath { get; set; }

        /// <summary>
        /// Anzahl der lokalen Audiodateien im Episodenordner.
        /// Dient als Grundlage für den Abgleich mit der Online-Trackzahl.
        /// </summary>
        public int? LocalTrackCount { get; set; }

        /// <summary>
        /// Ergebnis des Abgleichs zwischen lokalen und Online-Tracks.
        /// Standardmäßig <see cref="TrackMatchKind.NotMatched"/> bis ein Scan durchgeführt wurde.
        /// </summary>
        public TrackMatchKind TrackMatchKind { get; set; } = TrackMatchKind.NotMatched;

        /// <summary>
        /// URL zum Öffnen der Folge beim Provider (Spotify/Apple Music).
        /// Wird beim Online-Import gesetzt. Null bei lokalen Folgen.
        /// Beispiel: "https://open.spotify.com/album/abc123"
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Entity spiegelt DB-Spalte ProviderUrl (Spotify/Apple-Music-Deep-Link als TEXT); Uri-Umwandlung würde EF-Core-Mapping erfordern ohne fachlichen Mehrwert.")]
        public string? ProviderUrl { get; set; }

        /// <summary>
        /// URL zum Album-Cover beim Provider (Spotify/Apple Music).
        /// Wird beim Import gesetzt und dient als Fallback für die Cover-Suche,
        /// wenn <see cref="LocalCoverData"/> noch nicht geladen wurde.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Entity spiegelt DB-Spalte CoverImageUrl (Spotify/Apple-Music-Cover-URL als TEXT); Uri-Umwandlung würde EF-Core-Mapping erfordern ohne fachlichen Mehrwert.")]
        public string? CoverImageUrl { get; set; }

        /// <summary>
        /// Manuell zugewiesenes oder automatisch ermitteltes Folgen-Cover als Rohdaten.
        /// Getrennt von <c>Series.LocalCoverData</c> – das Folgen-Cover zeigt das episodenspezifische
        /// Artwork, das Serien-Cover das übergreifende Logo der Serie.
        /// Null wenn kein Cover vorhanden oder noch nicht geladen.
        /// </summary>
        public byte[]? LocalCoverData { get; set; }

        /// <summary>
        /// Zeitpunkt der letzten automatischen Cover-Suche.
        /// Verhindert wiederholtes Durchsuchen bei Episoden, für die kein Cover gefunden wurde.
        /// Null bedeutet: noch nie geprüft. Nach einer erfolglosen Suche wird der Zeitpunkt
        /// gesetzt und erst nach Ablauf des Cooldowns (7 Tage) erneut gesucht.
        /// </summary>
        public DateTime? CoverLastChecked { get; set; }

        /// <summary>
        /// Album-ID der Episode bei Spotify.
        /// Wird gesetzt, wenn die Episode von Spotify importiert wurde.
        /// </summary>
        public string? SpotifyAlbumId { get; set; }

        /// <summary>
        /// Album-ID der Episode bei Apple Music.
        /// Wird gesetzt, wenn die Episode von Apple Music importiert wurde.
        /// </summary>
        public string? AppleMusicAlbumId { get; set; }
    }
}