using EchoPlay.Core.Models;

namespace EchoPlay.Core.Abstractions
{
    /// <summary>
    /// Prüft für abonnierte Serien, ob online (iTunes) neue Folgen verfügbar sind,
    /// die lokal noch nicht vorhanden sind.
    /// Die Prüfung vergleicht die höchste Episodennummer aus iTunes-Albumnamen
    /// mit der höchsten Nummer in den lokalen Episodenordnern.
    /// </summary>
    public interface IOnlineEpisodeChecker
    {
        /// <summary>
        /// Prüft alle übergebenen Serien sequenziell gegen die iTunes API.
        /// Zwischen den API-Aufrufen wird ein Rate-Limiting-Delay eingehalten.
        /// Serien, deren Prüfung fehlschlägt (z.B. kein Netz), werden übersprungen.
        /// </summary>
        /// <param name="subscribedSeries">Die zu prüfenden Serien.</param>
        /// <param name="cancellationToken">Abbruchtoken für die Hintergrundprüfung.</param>
        /// <returns>
        /// Liste der Prüfergebnisse – nur für Serien, die erfolgreich geprüft werden konnten.
        /// Kann leer sein, wenn alle Prüfungen fehlschlagen.
        /// </returns>
        Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Prüft alle übergebenen Serien und sammelt Neuerscheinungen im angegebenen Zeitfenster.
        /// Alben mit <c>ReleaseDate</c> zwischen <paramref name="cutoffDate"/> und heute
        /// werden als <see cref="NewReleaseEpisode"/> in das Ergebnis aufgenommen.
        /// </summary>
        /// <param name="subscribedSeries">Die zu prüfenden Serien.</param>
        /// <param name="cutoffDate">Ältestes erlaubtes Veröffentlichungsdatum (UTC).</param>
        /// <param name="cancellationToken">Abbruchtoken für die Hintergrundprüfung.</param>
        /// <returns>
        /// Liste der Prüfergebnisse mit befüllten <see cref="OnlineEpisodeCheckResult.NewReleaseEpisodes"/>.
        /// </returns>
        Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            DateTime cutoffDate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Minimale Serieninformationen für die Online-Prüfung.
    /// Entkoppelt den Checker vom EF-Entity <c>Series</c>, damit keine Data-Abhängigkeit
    /// im Interface entsteht.
    /// </summary>
    public sealed class CheckableSeriesInfo
    {
        /// <summary>Datenbank-ID der Serie.</summary>
        public required Guid SeriesId { get; init; }

        /// <summary>Serientitel (für iTunes-Suche, falls keine Artist-ID vorhanden).</summary>
        public required string Title { get; init; }

        /// <summary>
        /// Apple Music Artist ID (als String). Null, wenn die Serie über Spotify importiert wurde
        /// und noch nie per iTunes gesucht wurde.
        /// </summary>
        public string? AppleMusicArtistId { get; init; }

        /// <summary>Lokaler Ordnerpfad der Serie. Null, wenn nicht lokal vorhanden.</summary>
        public string? LocalFolderPath { get; init; }

        /// <summary>URL zum Serien-Cover.</summary>
        public string? CoverImageUrl { get; init; }
    }
}
