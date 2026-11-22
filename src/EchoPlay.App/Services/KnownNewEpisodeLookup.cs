using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Findet die Folgen nach der letzten lokalen Nummer, die der Anbieter schon geliefert hat
    /// und die deshalb in der Datenbank stehen. Ohne Netz, darum auch bei „Nur offline".
    /// </summary>
    /// <remarks>
    /// Eine Serie, die lokal und beim Anbieter vorliegt, steht zweimal in der Datenbank: einmal mit
    /// dem Ordner, einmal als Online-Import. Die Titel können abweichen („Die drei Ausrufezeichen"
    /// und „Die drei !!!"), die Künstlerkennung des Anbieters ist dieselbe. Über sie wird das
    /// Gegenstück gefunden, zuerst über Apple Music, dann über Spotify.
    /// </remarks>
    internal sealed class KnownNewEpisodeLookup(
        ISeriesDataService seriesService,
        IEpisodeDataService episodeService)
    {
        /// <summary>
        /// Liefert die beim Anbieter bekannten Folgen mit einer Nummer über
        /// <paramref name="localHighest"/>, aufsteigend und je Nummer einmal.
        /// </summary>
        /// <param name="localSeries">Die Serie mit dem lokalen Ordner.</param>
        /// <param name="localHighest">Höchste Folgennummer im lokalen Ordner.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die neuen Folgen; leer, wenn es kein Gegenstück gibt.</returns>
        public async Task<IReadOnlyList<OnlineEpisodeInfo>> FindAfterAsync(
            Series localSeries, int localHighest, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(localSeries);

            Series? providerSeries = await FindProviderSeriesAsync(localSeries, cancellationToken);
            if (providerSeries is null)
            {
                return [];
            }

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(providerSeries.Id, cancellationToken);

            // Ist die lokale Serie selbst der Online-Import, stehen ihre lokalen Folgen in derselben
            // Liste. Was Dateien hat, ist vorhanden und gehört nicht in die neuen Folgen.
            return episodes
                .Where(episode => episode.EpisodeNumber > localHighest && (episode.LocalTrackCount ?? 0) == 0)
                .GroupBy(episode => episode.EpisodeNumber!.Value) // nach dem Filter oben nie null
                .OrderBy(group => group.Key)
                .Select(group => new OnlineEpisodeInfo { EpisodeNumber = group.Key, Title = group.First().Title })
                .ToList();
        }

        /// <summary>
        /// Führt die Folgen aus der Datenbank mit denen aus dem Live-Abgleich zusammen. Bei gleicher
        /// Nummer gewinnt die Datenbank: Ihr Titel ist der Folgentitel, der Live-Abgleich liefert
        /// den ganzen Albumnamen samt Serie.
        /// </summary>
        /// <param name="known">Folgen aus der Datenbank.</param>
        /// <param name="live">Folgen aus dem Live-Abgleich.</param>
        /// <returns>Alle Nummern aus beiden Quellen, aufsteigend.</returns>
        public static IReadOnlyList<OnlineEpisodeInfo> Merge(
            IReadOnlyList<OnlineEpisodeInfo> known, IReadOnlyList<OnlineEpisodeInfo> live)
        {
            ArgumentNullException.ThrowIfNull(known);
            ArgumentNullException.ThrowIfNull(live);

            Dictionary<int, OnlineEpisodeInfo> byNumber = [];
            foreach (OnlineEpisodeInfo episode in known.Concat(live))
            {
                _ = byNumber.TryAdd(episode.EpisodeNumber, episode);
            }

            return byNumber.Values.OrderBy(episode => episode.EpisodeNumber).ToList();
        }

        private async Task<Series?> FindProviderSeriesAsync(Series localSeries, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(localSeries.AppleMusicArtistId))
            {
                Series? byAppleMusic = await seriesService.GetOnlineImportedByAppleMusicArtistIdAsync(
                    localSeries.AppleMusicArtistId, cancellationToken);
                if (byAppleMusic is not null)
                {
                    return byAppleMusic;
                }
            }

            return string.IsNullOrWhiteSpace(localSeries.SpotifyArtistId)
                ? null
                : await seriesService.GetOnlineImportedBySpotifyArtistIdAsync(localSeries.SpotifyArtistId, cancellationToken);
        }
    }
}
