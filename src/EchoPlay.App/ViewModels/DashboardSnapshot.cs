using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die gemeinsame Datengrundlage eines Startseiten-Aufbaus: abonnierte Serien,
    /// Favoriten in ihrer gespeicherten Reihenfolge, alle Wiedergabestände und der
    /// Offline-Zustand.
    /// </summary>
    /// <remarks>
    /// Alle Abschnitte der Startseite leiten sich hieraus ab. Deshalb wird der Bestand
    /// einmal am Stück gelesen statt je Abschnitt erneut — sonst entstünde für jede Kachel
    /// eine eigene Abfrage.
    /// </remarks>
    /// <param name="SubscribedSeries">Alle abonnierten Serien.</param>
    /// <param name="FavoriteSeries">Die Favoriten in Anzeigereihenfolge.</param>
    /// <param name="AllStates">Alle Wiedergabestände.</param>
    /// <param name="StateByEpisodeId">Dieselben Stände, nach Folgen-Kennung erreichbar.</param>
    /// <param name="OfflineMode">Ob die Anwendung ohne Netz arbeitet.</param>
    internal sealed record DashboardSnapshot(
        IReadOnlyList<Series> SubscribedSeries,
        IReadOnlyList<Series> FavoriteSeries,
        IReadOnlyList<PlaybackState> AllStates,
        IReadOnlyDictionary<Guid, PlaybackState> StateByEpisodeId,
        bool OfflineMode)
    {
        /// <summary>Abschnittsname, unter dem die Reihenfolge der Neuerscheinungen liegt.</summary>
        private const string SectionNewReleases = "Neuerscheinungen";

        /// <summary>Ob überhaupt eine Serie abonniert ist. Sonst führt die Startseite ins Einrichten.</summary>
        public bool HasSubscribedSeries => SubscribedSeries.Count > 0;

        /// <summary>Ob mindestens eine Serie favorisiert ist.</summary>
        public bool HasFavoriteSeries => FavoriteSeries.Count > 0;

        /// <summary>Ob mindestens eine Serie auf neue Folgen überwacht wird.</summary>
        public bool HasWatchedSeries => SubscribedSeries.Any(s => s.IsWatched);

        /// <summary>
        /// Die Folgen, deren Cover in diesem Durchgang sichtbar werden können — alles, wozu
        /// ein Wiedergabestand vorliegt.
        /// </summary>
        public IReadOnlyList<Guid> RelevantEpisodeIds
        {
            get
            {
                List<Guid> ids = new(AllStates.Count);
                foreach (PlaybackState state in AllStates)
                {
                    ids.Add(state.EpisodeId);
                }

                return ids;
            }
        }

        /// <summary>
        /// Liest die Datengrundlage. Die Serien werden bei jedem Aufruf frisch geholt — die
        /// Überwachung kann sich während der Sitzung ändern.
        /// </summary>
        /// <param name="seriesService">Zugriff auf Serien und Favoriten.</param>
        /// <param name="stateService">Zugriff auf die Wiedergabestände.</param>
        /// <param name="settingsService">Nur nötig, solange der Startlauf kein Ergebnis geliefert hat.</param>
        /// <param name="positionService">Die gespeicherte Reihenfolge der Favoriten.</param>
        /// <param name="startupResult">Das Ergebnis der Prüfungen im Startbild, sofern es schon vorliegt.</param>
        /// <param name="logger">Protokollkanal für die Reihenfolge der Favoriten.</param>
        /// <returns>Die gelesene Datengrundlage.</returns>
        public static async Task<DashboardSnapshot> LoadAsync(
            ISeriesDataService seriesService,
            IPlaybackStateDataService stateService,
            IAppSettingsDataService settingsService,
            IDashboardPositionDataService positionService,
            StartupResult? startupResult,
            ILogger logger)
        {
            IReadOnlyList<Series> subscribed = await seriesService.GetSubscribedAsync();
            bool offlineMode = await ResolveOfflineModeAsync(settingsService, startupResult);

            IReadOnlyList<Series> favorites = await LoadFavoritesInOrderAsync(seriesService, positionService, logger);

            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
            Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
            foreach (PlaybackState ps in allStates)
            {
                stateByEpisodeId[ps.EpisodeId] = ps;
            }

            return new DashboardSnapshot(subscribed, favorites, allStates, stateByEpisodeId, offlineMode);
        }

        /// <summary>
        /// Ermittelt den Offline-Zustand. Der Startlauf hat ihn bereits geprüft; nur wenn
        /// sein Ergebnis noch nicht vorliegt, wird die Einstellung selbst gelesen.
        /// </summary>
        private static async Task<bool> ResolveOfflineModeAsync(
            IAppSettingsDataService settingsService, StartupResult? startupResult)
        {
            if (startupResult is not null)
            {
                return !startupResult.IsOnlineAvailable || startupResult.Settings.OfflineMode;
            }

            AppSettings appSettings = await settingsService.GetAsync();
            return appSettings.OfflineMode;
        }

        /// <summary>
        /// Liest die Favoriten und bringt sie in die vom Nutzer gespeicherte Reihenfolge.
        /// Serien ohne gespeicherte Position hängen alphabetisch hinten an.
        /// </summary>
        private static async Task<IReadOnlyList<Series>> LoadFavoritesInOrderAsync(
            ISeriesDataService seriesService,
            IDashboardPositionDataService positionService,
            ILogger logger)
        {
            IReadOnlyList<Series> favoritesRaw = await seriesService.GetFavoritesAsync();

            IReadOnlyList<DashboardPosition> savedPositions = await positionService.GetBySectionAsync(SectionNewReleases);

            Dictionary<Guid, int> positionBySeriesId = new(savedPositions.Count);
            foreach (DashboardPosition dp in savedPositions)
            {
                positionBySeriesId[dp.SeriesId] = dp.Position;
            }

            List<Series> ordered = [.. favoritesRaw
                .OrderBy(s => positionBySeriesId.TryGetValue(s.Id, out int pos) ? pos : int.MaxValue)
                .ThenBy(s => s.Title)];

            foreach (Series s in ordered)
            {
                string posText = positionBySeriesId.TryGetValue(s.Id, out int p)
                    ? p.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "keine";
                logger.Debug(() => $"Favorit: '{s.Title}' – Position={posText}");
            }

            return ordered;
        }
    }
}
