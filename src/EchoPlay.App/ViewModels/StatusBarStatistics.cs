using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Liest, was die Info-Leiste anzeigt, aus der Datenbank und den Zugangsdaten.
    /// </summary>
    internal static class StatusBarStatistics
    {
        /// <summary>
        /// Zählt die abonnierten Serien und ihre Folgen nach Hörstand.
        /// </summary>
        /// <param name="scope">Bereich mit den Datendiensten.</param>
        /// <param name="today">Der heutige Tag (UTC); spätere Folgen gelten noch nicht als neu.</param>
        /// <returns>Die Zahlen der Info-Leiste.</returns>
        public static async Task<LibraryCounts> CountAsync(IServiceScope scope, DateTime today)
        {
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync();

            // Alle Wiedergabestände einmalig laden, das vermeidet N+1-Queries beim Episoden-Abgleich
            IReadOnlyList<PlaybackState> allStates = await playbackService.GetAllAsync();
            HashSet<Guid> completedEpisodeIds = [.. allStates.Where(s => s.IsCompleted).Select(s => s.EpisodeId)];

            // Alle Episoden der abonnierten Serien in einem einzigen Query laden,
            // denn ein Query pro Serie wäre ein N+1-Problem.
            IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(subscribedSeries.Select(s => s.Id).ToArray());

            int finished = 0;
            int unfinished = 0;
            int newCount = 0;

            foreach (Episode episode in allEpisodes)
            {
                if (completedEpisodeIds.Contains(episode.Id))
                {
                    finished++;
                    continue;
                }

                unfinished++;

                // "Neu" = bereits veröffentlicht, aber noch nicht gehört.
                // Zukünftige Episoden (ReleaseDate > heute) werden hier nicht gezählt.
                if (episode.ReleaseDate.HasValue && episode.ReleaseDate.Value.Date <= today)
                {
                    newCount++;
                }
            }

            return new LibraryCounts(subscribedSeries.Count, finished, unfinished, newCount);
        }

        /// <summary>
        /// Ermittelt den anzuzeigenden Provider-Namen credential-bewusst. Ist Spotify gewählt,
        /// aber sind keine Spotify-Credentials hinterlegt (oder nicht entschlüsselbar → DPAPI-Purge),
        /// lenkt die Suche transparent auf Apple Music (siehe <c>SeriesProviderResolver</c>).
        /// Die Info-Leiste darf dann nicht fälschlich „Spotify" als verbunden zeigen, sondern den
        /// effektiv nutzbaren Zustand („Spotify getrennt – Apple Music aktiv").
        /// </summary>
        /// <param name="scope">Bereich mit den Diensten für Zugangsdaten und Texte.</param>
        /// <param name="provider">Der in den AppSettings gewählte Provider.</param>
        /// <returns>Der anzuzeigende Provider-Text.</returns>
        public static async Task<string> ResolveProviderDisplayAsync(IServiceScope scope, ProviderType provider)
        {
            switch (provider)
            {
                case ProviderType.Spotify:
                    // GetService (nicht GetRequiredService): fehlt der Provider (z.B. in Tests
                    // ohne Spotify-Registrierung), bleibt es beim schlichten "Spotify"-Hinweis.
                    ISpotifyClientCredentialsProvider? credentialsProvider =
                        scope.ServiceProvider.GetService<ISpotifyClientCredentialsProvider>();
                    if (credentialsProvider is not null && await credentialsProvider.GetAsync() is null)
                    {
                        ILocalizationService localization =
                            scope.ServiceProvider.GetRequiredService<ILocalizationService>();
                        return localization.Get("StatusBarSpotifyDisconnected");
                    }
                    return "Spotify";
                case ProviderType.AppleMusic:
                    return "Apple Music";
                case ProviderType.Both:
                    return "Spotify + Apple Music";
                default:
                    return string.Empty;
            }
        }
    }
}
