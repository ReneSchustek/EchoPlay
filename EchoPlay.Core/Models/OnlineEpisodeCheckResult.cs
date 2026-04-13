using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Ergebnis der Online-Prüfung für eine einzelne Serie.
    /// Vergleicht die höchste lokal vorhandene Episodennummer mit der höchsten
    /// online verfügbaren Nummer (ermittelt über iTunes/Apple Music).
    /// </summary>
    public sealed class OnlineEpisodeCheckResult
    {
        /// <summary>ID der geprüften Serie in der lokalen Datenbank.</summary>
        public required Guid SeriesId { get; init; }

        /// <summary>Anzeigename der Serie (für Dashboard-Kacheln).</summary>
        public required string SeriesTitle { get; init; }

        /// <summary>
        /// Höchste online erkannte Episodennummer.
        /// Wird aus den Albumnamen bei iTunes extrahiert (Regex auf die erste Zahl im Titel).
        /// </summary>
        public int OnlineHighestNumber { get; init; }

        /// <summary>
        /// Höchste lokal vorhandene Episodennummer.
        /// Wird aus den Ordnernamen im Serienverzeichnis extrahiert.
        /// 0, wenn kein lokaler Ordner vorhanden ist.
        /// </summary>
        public int LocalHighestNumber { get; init; }

        /// <summary>
        /// Anzahl neuer Folgen, die online verfügbar aber lokal nicht vorhanden sind.
        /// Berechnet als <c>Max(0, OnlineHighestNumber - LocalHighestNumber)</c>.
        /// </summary>
        public int NewEpisodesCount { get; init; }

        /// <summary>
        /// Angekündigte Folgen mit Erscheinungsdatum in der Zukunft.
        /// Jeder Eintrag enthält den Albumnamen und das geplante Veröffentlichungsdatum.
        /// </summary>
        public IReadOnlyList<AnnouncedEpisode> AnnouncedEpisodes { get; init; } = [];

        /// <summary>
        /// Konkrete Neuerscheinungen innerhalb des konfigurierten Zeitfensters.
        /// Enthält alle iTunes-Alben mit ReleaseDate im Bereich Cutoff bis heute,
        /// sortiert nach Datum absteigend (neueste zuerst).
        /// </summary>
        public IReadOnlyList<NewReleaseEpisode> NewReleaseEpisodes { get; init; } = [];

        /// <summary>
        /// Konkrete fehlende Folgen zwischen <see cref="LocalHighestNumber"/> und <see cref="OnlineHighestNumber"/>.
        /// Enthält für jede online gefundene Folgennummer &gt; lokal den iTunes-Albumnamen.
        /// Leer, wenn <see cref="NewEpisodesCount"/> == 0.
        /// </summary>
        public IReadOnlyList<MissingOnlineEpisode> MissingOnlineEpisodes { get; init; } = [];

        /// <summary>URL zum Serien-Cover (für Dashboard-Kacheln).</summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO spiegelt externes API-Format (iTunes/Spotify JSON); Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
        public string? CoverUrl { get; init; }

        /// <summary>Zeitpunkt dieser Prüfung (UTC).</summary>
        public DateTime CheckedAtUtc { get; init; }
    }

    /// <summary>
    /// Eine angekündigte, aber noch nicht erschienene Episode.
    /// </summary>
    public sealed class AnnouncedEpisode
    {
        /// <summary>Albumname aus iTunes (z.B. "Die drei ??? - Folge 238 - ...").</summary>
        public required string Title { get; init; }

        /// <summary>Geplantes Veröffentlichungsdatum.</summary>
        public required DateTime ReleaseDate { get; init; }
    }

    /// <summary>
    /// Eine bei iTunes gefundene Episode innerhalb des Neuerscheinungen-Zeitfensters.
    /// Enthält alle Informationen, die das Dashboard für die Kachel-Anzeige braucht:
    /// Titel, Folgennummer, Erscheinungsdatum und Cover-URL.
    /// </summary>
    public sealed class NewReleaseEpisode
    {
        /// <summary>Albumname aus iTunes (z.B. "Die drei ??? - Folge 238 - Titel").</summary>
        public required string Title { get; init; }

        /// <summary>
        /// Extrahierte Folgennummer (z.B. 238). Null, wenn keine Nummer erkannt wurde.
        /// </summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Veröffentlichungsdatum aus iTunes (UTC).</summary>
        public required DateTime ReleaseDate { get; init; }

        /// <summary>
        /// URL zum Album-Cover in 100×100 Pixel. Kann für die Kachel-Anzeige auf
        /// größere Auflösungen hochskaliert werden (iTunes-URL-Pattern: /100x100bb → /600x600bb).
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO spiegelt externes API-Format (iTunes/Spotify JSON); Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
        public string? CoverUrl { get; init; }

        /// <summary>iTunes-Collection-ID – dient als eindeutiger Schlüssel für dieses Album.</summary>
        public long CollectionId { get; init; }
    }

    /// <summary>
    /// Eine bei iTunes gefundene Folge, die lokal noch nicht vorhanden ist.
    /// Wird in <see cref="OnlineEpisodeCheckResult.MissingOnlineEpisodes"/> gesammelt.
    /// </summary>
    public sealed class MissingOnlineEpisode
    {
        /// <summary>Extrahierte Folgennummer aus dem Albumnamen.</summary>
        public int EpisodeNumber { get; init; }

        /// <summary>Vollständiger Albumname aus iTunes (z.B. "Die drei ??? - Folge 230 - Titel").</summary>
        public required string AlbumTitle { get; init; }
    }
}
