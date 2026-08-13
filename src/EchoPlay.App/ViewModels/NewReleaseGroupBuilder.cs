using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Baut den Neuerscheinungs-Abschnitt der Startseite: aus den zwischengespeicherten
    /// Einträgen des Anbieters werden Kacheln, die nach Monat gruppiert erscheinen.
    /// „Angekündigt" — alles mit Datum in der Zukunft — steht als eigene Gruppe ganz oben,
    /// darunter die Monate mit dem neuesten zuerst. Bereits gehörte Folgen fallen heraus.
    /// </summary>
    internal sealed class NewReleaseGroupBuilder
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DashboardCoverProvider _coverProvider;
        private readonly NewEpisodeCardServices _cardServices;
        private readonly IClock _clock;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Aufbau mit den Diensten, die Kacheln und Cover brauchen.
        /// </summary>
        /// <param name="scopeFactory">Für die Abfragen je Durchgang.</param>
        /// <param name="coverProvider">Beschafft die Cover.</param>
        /// <param name="cardServices">Die Dienste, die jede Kachel für ihre Befehle braucht.</param>
        /// <param name="clock">Zeitquelle für die Abgrenzung „angekündigt".</param>
        /// <param name="logger">Protokollkanal.</param>
        public NewReleaseGroupBuilder(
            IServiceScopeFactory scopeFactory,
            DashboardCoverProvider coverProvider,
            NewEpisodeCardServices cardServices,
            IClock clock,
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverProvider = coverProvider;
            _cardServices = cardServices;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>
        /// Baut die Gruppen aus den zwischengespeicherten Einträgen. Schlägt der Zugriff
        /// fehl, bleibt der Abschnitt leer und der Vorfall steht im Protokoll — die
        /// Startseite selbst darf daran nicht scheitern.
        /// </summary>
        /// <param name="subscribedSeries">Die abonnierten Serien; nur überwachte werden gezeigt.</param>
        /// <param name="cancellationToken">Bricht den Aufbau ab.</param>
        /// <returns>Die Gruppen in Anzeigereihenfolge.</returns>
        public async Task<IReadOnlyList<NewEpisodesGroupViewModel>> BuildAsync(
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ICachedNewReleaseDataService cacheService =
                    scope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();

                IReadOnlyList<CachedNewRelease> cached = await cacheService.GetAllAsync(cancellationToken);
                if (cached.Count == 0)
                {
                    return [];
                }

                IEpisodeDataService episodeService =
                    scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService =
                    scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                IReadOnlyList<(NewEpisodeCardViewModel Card, DateTime ReleaseDate)> cards =
                    await BuildCardsAsync(cached, subscribedSeries, episodeService, stateService, cancellationToken);

                return GroupByMonth(cards);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning("Neuerscheinungen aus Cache konnten nicht geladen werden: {Reason}", ex.Message);
                return [];
            }
            catch (System.IO.IOException ex)
            {
                _logger.Warning("Neuerscheinungen aus Cache konnten nicht geladen werden: {Reason}", ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Wandelt die Einträge in Kacheln um. Gehörte Folgen und Einträge zu nicht
        /// überwachten Serien fallen dabei heraus.
        /// </summary>
        private async Task<IReadOnlyList<(NewEpisodeCardViewModel Card, DateTime ReleaseDate)>> BuildCardsAsync(
            IReadOnlyList<CachedNewRelease> cached,
            IReadOnlyList<Series> subscribedSeries,
            IEpisodeDataService episodeService,
            IPlaybackStateDataService stateService,
            CancellationToken cancellationToken)
        {
            DateTime today = _clock.UtcNow.Date;
            LocalEpisodeIndex index = await LocalEpisodeIndex.CreateAsync(episodeService, stateService, cancellationToken);
            List<(NewEpisodeCardViewModel Card, DateTime ReleaseDate)> cards = [];

            foreach (CachedNewRelease entry in cached)
            {
                Series? series = FindSeriesById(subscribedSeries, entry.SeriesId);

                // Nur überwachte Serien anzeigen — nicht überwachte werden übergangen.
                if (series is null || !series.IsWatched)
                {
                    continue;
                }

                LocalEpisodeMatch match = await index.MatchAsync(series.Id, entry.EpisodeNumber, cancellationToken);
                if (match.IsCompleted)
                {
                    continue;
                }

                cards.Add((await BuildCardAsync(entry, series, match, entry.ReleaseDate.Date > today), entry.ReleaseDate));
            }

            return cards;
        }

        /// <summary>Baut eine einzelne Kachel samt Cover und merkt sie ggf. zum Nachtragen vor.</summary>
        private async Task<NewEpisodeCardViewModel> BuildCardAsync(
            CachedNewRelease entry,
            Series series,
            LocalEpisodeMatch match,
            bool isAnnounced)
        {
            (BitmapImage? cover, bool hasEpisodeCover) = await ResolveCoverAsync(entry, series, match.EpisodeId);

            // Ohne lokalen Treffer gibt es noch gar keine Folgen-Id — die Kachel bekommt eine
            // eigene, damit die Liste eindeutig bleibt.
            Guid cardEpisodeId = match.EpisodeId != Guid.Empty ? match.EpisodeId : Guid.NewGuid();

            NewEpisodeCardViewModel card = new(
                episodeId: cardEpisodeId,
                seriesId: series.Id,
                seriesName: series.Title,
                episodeTitle: entry.Title,
                coverImage: cover,
                status: PlaybackStatus.NotStarted,
                progressPercent: 0,
                hasLocalTrack: match.HasLocalTrack,
                isAnnounced: isAnnounced,
                scopeFactory: _scopeFactory,
                errorDialogService: _cardServices.ErrorDialogService,
                confirmationDialogService: _cardServices.ConfirmationDialogService,
                playerService: _cardServices.PlayerService,
                episodeNumber: entry.EpisodeNumber,
                releaseDate: entry.ReleaseDate,
                localizationService: _cardServices.LocalizationService,
                clock: _clock,
                // Die Sammlungs-Kennung des Anbieters ist bei Apple Music die Album-Kennung.
                // Sie liegt im Eintrag vor — anders als eine Folgen-Id, die es für eine reine
                // Neuerscheinung noch gar nicht gibt.
                appleMusicAlbumId: entry.CollectionId > 0
                    ? entry.CollectionId.ToString(CultureInfo.InvariantCulture)
                    : null);

            // Nur lokal bekannte Folgen kommen ins Nachtragen — ein reiner Eintrag ohne
            // lokalen Treffer hat keine durchsuchbare Cover-Quelle.
            if (!hasEpisodeCover && match.EpisodeId != Guid.Empty)
            {
                _coverProvider.TrackPending(match.EpisodeId, card);
            }

            return card;
        }

        /// <summary>
        /// Ermittelt das Cover einer Neuerscheinung.
        /// Reihenfolge: Folgen-Cover aus der Datenbank, sonst das Album-Bild des Anbieters
        /// (dessen Adresse lässt sich auf eine höhere Auflösung ziehen), sonst das Serien-Cover.
        /// </summary>
        private async Task<(BitmapImage? Cover, bool HasEpisodeCover)> ResolveCoverAsync(
            CachedNewRelease entry, Series series, Guid episodeId)
        {
            BitmapImage? cover = await _coverProvider.TryGetCachedEpisodeCoverAsync(episodeId);
            if (cover is not null)
            {
                return (cover, true);
            }

            if (!string.IsNullOrEmpty(entry.CoverUrl))
            {
                string highResCoverUrl = entry.CoverUrl.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);
                return (new BitmapImage(new Uri(highResCoverUrl)), false);
            }

            return (await _coverProvider.BuildSeriesCoverAsync(series), false);
        }

        /// <summary>
        /// Gruppiert die Kacheln: „Angekündigt" zuerst, danach die Monate absteigend.
        /// </summary>
        private List<NewEpisodesGroupViewModel> GroupByMonth(
            IReadOnlyList<(NewEpisodeCardViewModel Card, DateTime ReleaseDate)> cards)
        {
            // Die Monatsnamen kommen aus der Oberflächensprache, nicht aus der Systemkultur:
            // Letztere folgt dem Windows-Gebietsschema und lief bei abweichender App-Sprache
            // auseinander — englische Oberfläche, deutsche Monatsnamen.
            string[] monthNames = ResolveUiCulture().DateTimeFormat.MonthNames;
            List<NewEpisodesGroupViewModel> groups = [];

            List<NewEpisodeCardViewModel> announced = [.. cards.Where(e => e.Card.IsAnnounced).Select(e => e.Card)];
            if (announced.Count > 0)
            {
                // Die Überschrift kommt aus den Ressourcen: Das Kennzeichen auf derselben
                // Kachel war bereits übersetzt, die Überschrift blieb deutsch — auf englischer
                // Oberfläche standen beide Sprachen nebeneinander.
                string announcedLabel = _cardServices.LocalizationService?.Get("DashboardGroupAnnounced") ?? "Angekündigt";
                groups.Add(new NewEpisodesGroupViewModel(announcedLabel, 0, announced));
            }

            IEnumerable<IGrouping<(int Year, int Month), (NewEpisodeCardViewModel Card, DateTime ReleaseDate)>> monthGroups =
                cards
                    .Where(e => !e.Card.IsAnnounced)
                    .GroupBy(e => (e.ReleaseDate.Year, e.ReleaseDate.Month))
                    .OrderByDescending(g => g.Key.Year)
                    .ThenByDescending(g => g.Key.Month);

            foreach (IGrouping<(int Year, int Month), (NewEpisodeCardViewModel Card, DateTime ReleaseDate)> monthGroup in monthGroups)
            {
                // Die Monatsnamen sind nullbasiert, die Monatszahl beginnt bei eins.
                string label = $"{monthNames[monthGroup.Key.Month - 1]} {monthGroup.Key.Year}";
                groups.Add(new NewEpisodesGroupViewModel(label, BuildSortKey(monthGroup.Key), [.. monthGroup.Select(e => e.Card)]));
            }

            return groups;
        }

        /// <summary>
        /// Bildet den Sortierschlüssel eines Monats: der negative Tageswert seines Anfangs,
        /// damit der neueste Monat oben steht.
        /// </summary>
        private static int BuildSortKey((int Year, int Month) month)
        {
            DateTime monthStart = new(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return -(int)(monthStart - DateTime.UnixEpoch).TotalDays;
        }

        /// <summary>
        /// Liefert die Kultur der Oberflächensprache. Grundlage ist die gesetzte
        /// Sprachvorgabe; erst wenn die fehlt, gilt die Systemkultur.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Reine Anzeige-Kultur für Monatsnamen: ein unbekannter Sprachcode oder eine nicht verfügbare Plattform-API darf den Dashboard-Aufbau nicht abbrechen – Fallback ist die aktuelle Kultur.")]
        private static CultureInfo ResolveUiCulture()
        {
            try
            {
                string primary = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
                if (!string.IsNullOrWhiteSpace(primary))
                {
                    return new CultureInfo(primary);
                }
            }
            catch (Exception)
            {
                // Fällt unten auf die aktuelle Kultur zurück.
            }

            return CultureInfo.CurrentCulture;
        }

        /// <summary>Sucht eine Serie in einer Liste anhand ihrer Kennung.</summary>
        private static Series? FindSeriesById(IReadOnlyList<Series> series, Guid id)
        {
            foreach (Series s in series)
            {
                if (s.Id == id)
                {
                    return s;
                }
            }

            return null;
        }

    }
}
