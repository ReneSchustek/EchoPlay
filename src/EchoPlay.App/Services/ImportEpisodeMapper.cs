using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bildet die Antwort eines Anbieters auf Serien und Folgen des Bestands ab: doppelte
    /// Treffer verwerfen, bekannte Folgen überspringen, neue anlegen.
    /// </summary>
    /// <param name="logger">Protokoll des Imports.</param>
    internal sealed class ImportEpisodeMapper(ILogger logger)
    {
        /// <summary>
        /// Bildet aus der Anbieter-Antwort die Folgen ab, die der Bestand noch nicht kennt.
        /// Jede angelegte Folge wandert sofort in den Index, damit sie kein zweites Mal entsteht.
        /// </summary>
        /// <param name="providerEpisodes">Die entdoppelte Anbieter-Antwort.</param>
        /// <param name="existing">Der vorhandene Bestand der Serie.</param>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
        /// <param name="seriesTitle">Serientitel für die Protokollzeile.</param>
        /// <returns>Die anzulegenden Folgen in Anbieter-Reihenfolge.</returns>
        public List<Episode> MapMissingEpisodes(
            List<ImportEpisode> providerEpisodes,
            ExistingEpisodeIndex existing,
            Guid seriesId,
            string seriesTitle)
        {
            List<Episode> missing = new(providerEpisodes.Count);
            int known = 0;

            foreach (ImportEpisode importEpisode in providerEpisodes)
            {
                if (existing.Contains(importEpisode))
                {
                    known++;
                    continue;
                }

                Episode mapped = MapToEpisode(importEpisode, seriesId);
                existing.Add(mapped);
                missing.Add(mapped);
            }

            if (known > 0)
            {
                logger.Debug(() =>
                    $"Re-Import \"{seriesTitle}\": {known} bereits vorhandene Folgen übersprungen, {missing.Count} neu.");
            }

            return missing;
        }

        /// <summary>
        /// Filtert Episoden mit doppelter <see cref="ImportEpisode.SourceEpisodeId"/> heraus
        /// und behält nur das erste Vorkommen. Nötig, weil iTunes-Lookups bei Compilation-/
        /// Various-Artists-Alben dasselbe Album mehrfach liefern können. Ohne diese Stufe
        /// entstünden mehrere Episode-Zeilen mit identischer Provider-ID.
        /// Loggt eine Warnung, wenn Duplikate verworfen wurden, damit Provider-Anomalien
        /// im Protokoll sichtbar bleiben.
        /// </summary>
        public List<ImportEpisode> DeduplicateBySourceEpisodeId(
            IReadOnlyList<ImportEpisode> episodes,
            string seriesTitle)
        {
            HashSet<string> seenIds = new(StringComparer.Ordinal);
            List<ImportEpisode> unique = new(episodes.Count);
            int skipped = 0;

            foreach (ImportEpisode episode in episodes)
            {
                if (seenIds.Add(episode.SourceEpisodeId))
                {
                    unique.Add(episode);
                    continue;
                }

                skipped++;
            }

            if (skipped > 0)
            {
                logger.Warning("Import: {SkippedCount} doppelte Episoden-Treffer für \"{SeriesTitle}\" verworfen.", skipped, seriesTitle);
            }

            return unique;
        }

        /// <summary>
        /// Erstellt eine <see cref="Series"/>-Entität aus einem <see cref="ImportSeries"/>-Modell.
        /// Setzt die provider-spezifische Artist-ID anhand der Source-Bezeichnung.
        /// Import und Abonnement sind dasselbe Konzept: Jede importierte Serie ist direkt abonniert
        /// und erscheint sofort im Dashboard und in der Mediathek.
        /// </summary>
        /// <param name="importSeries">Das Anbieter-Modell, aus dem die Entität entsteht.</param>
        /// <param name="watchedTitles">Normalisierte Titel mit früher aktivierter Überwachung.</param>
        public static Series MapToSeries(ImportSeries importSeries, IReadOnlySet<string> watchedTitles)
        {
            return new Series
            {
                Title = importSeries.Title,
                Description = importSeries.Description,
                CoverImageUrl = importSeries.CoverImageUrl?.OriginalString,
                SpotifyArtistId = importSeries.Source == ProviderKeys.Spotify ? importSeries.SourceSeriesId : null,
                AppleMusicArtistId = importSeries.Source == ProviderKeys.AppleMusic ? importSeries.SourceSeriesId : null,
                IsOnlineImported = true,
                IsSubscribed = true,
                IsWatched = watchedTitles.Contains(
                    EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(importSeries.Title))
            };
        }

        /// <summary>
        /// Erstellt eine <see cref="Episode"/>-Entität aus einem <see cref="ImportEpisode"/>-Modell.
        /// Setzt die provider-spezifische Album-ID anhand der Source-Bezeichnung.
        /// </summary>
        /// <param name="importEpisode">Das Anbieter-Modell, aus dem die Entität entsteht.</param>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
        public static Episode MapToEpisode(ImportEpisode importEpisode, Guid seriesId)
        {
            return new Episode
            {
                SeriesId = seriesId,
                Title = importEpisode.Title,
                EpisodeNumber = importEpisode.EpisodeNumber,
                ReleaseDate = importEpisode.ReleaseDate,
                Duration = importEpisode.Duration,
                ProviderUrl = importEpisode.ProviderUrl?.OriginalString,
                CoverImageUrl = importEpisode.CoverImageUrl?.OriginalString,
                SpotifyAlbumId = importEpisode.Source == ProviderKeys.Spotify ? importEpisode.SourceEpisodeId : null,
                AppleMusicAlbumId = importEpisode.Source == ProviderKeys.AppleMusic ? importEpisode.SourceEpisodeId : null,
            };
        }
    }
}
