using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Http;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Liefert dem Kalenderabschnitt die Kacheln eines Monats.
    /// </summary>
    internal interface IReleaseCalendarSource
    {
        /// <summary>Baut die Kacheln eines Monats, nach Erscheinungstag sortiert.</summary>
        /// <param name="month">Der Monat in Ortszeit.</param>
        /// <param name="cancellationToken">Bricht den Aufbau ab.</param>
        /// <returns>Die Kacheln; leer, wenn nichts ansteht.</returns>
        Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadMonthAsync(ReleaseCalendarMonth month, CancellationToken cancellationToken);

        /// <summary>
        /// Baut die Kacheln aller angekündigten Folgen: alles, was ab morgen in der Zeitzone des
        /// Nutzers erscheint, nach Erscheinungstag sortiert.
        /// </summary>
        /// <param name="cancellationToken">Bricht den Aufbau ab.</param>
        /// <returns>Die Kacheln; leer, wenn nichts angekündigt ist.</returns>
        Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadAnnouncedAsync(CancellationToken cancellationToken);

        /// <summary>Liefert den Monat der spätesten bekannten Veröffentlichung.</summary>
        /// <param name="cancellationToken">Bricht die Abfrage ab.</param>
        /// <returns>Der Monat, oder <c>null</c> bei leerem Kalender.</returns>
        Task<ReleaseCalendarMonth?> GetLatestMonthAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Liest den Veröffentlichungskalender aus der Datenbank und gleicht jeden Eintrag mit
    /// dem Bestand ab: lokal vorhanden, nur in der Online-Mediathek oder noch gar nicht.
    /// </summary>
    internal sealed class ReleaseCalendarSource : IReleaseCalendarSource
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClock _clock;
        private readonly TimeZoneInfo _timeZone;
        private readonly CultureInfo _culture;
        private readonly ILocalizationService? _localizationService;

        /// <summary>
        /// Initialisiert die Quelle.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Abfrage einen Bereich.</param>
        /// <param name="clock">Zeitquelle für die Abgrenzung „angekündigt".</param>
        /// <param name="timeZone">Zeitzone für Monatsgrenzen und Erscheinungstag.</param>
        /// <param name="culture">Kultur der Oberfläche für das Datum.</param>
        /// <param name="localizationService">Liefert die Anzeigetexte; ohne ihn gelten deutsche Ersatztexte.</param>
        public ReleaseCalendarSource(
            IServiceScopeFactory scopeFactory,
            IClock clock,
            TimeZoneInfo timeZone,
            CultureInfo culture,
            ILocalizationService? localizationService)
        {
            _scopeFactory = scopeFactory;
            _clock = clock;
            _timeZone = timeZone;
            _culture = culture;
            _localizationService = localizationService;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadMonthAsync(
            ReleaseCalendarMonth month, CancellationToken cancellationToken)
            => LoadBetweenAsync(month.StartUtc(_timeZone), month.EndUtc(_timeZone), cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadAnnouncedAsync(CancellationToken cancellationToken)
            => LoadBetweenAsync(StartOfTomorrowUtc(), DateTime.MaxValue, cancellationToken);

        /// <summary>
        /// Der Beginn des morgigen Tags in der Zeitzone des Nutzers, als UTC. Ab da gilt eine
        /// Folge als angekündigt, wie beim Kennzeichen der Kachel.
        /// </summary>
        internal DateTime StartOfTomorrowUtc()
        {
            DateTime localTomorrow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _timeZone).Date.AddDays(1);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTomorrow, DateTimeKind.Unspecified), _timeZone);
        }

        /// <summary>Baut die Kacheln eines Zeitraums und gleicht sie mit dem Bestand ab.</summary>
        private async Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadBetweenAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            IReadOnlyList<ReleaseCalendarEntry> entries = await services.GetRequiredService<IReleaseCalendarDataService>()
                .GetBetweenAsync(fromUtc, toUtc, cancellationToken);
            if (entries.Count == 0)
            {
                return [];
            }

            LocalEpisodeIndex index = await LocalEpisodeIndex.CreateAsync(
                services.GetRequiredService<IEpisodeDataService>(),
                services.GetRequiredService<IPlaybackStateDataService>(),
                cancellationToken);

            List<ReleaseCalendarItemViewModel> items = new(entries.Count);
            foreach (ReleaseCalendarEntry entry in entries)
            {
                LocalEpisodeMatch match = await index.MatchAsync(entry.SeriesId, entry.EpisodeNumber, cancellationToken);
                items.Add(BuildItem(entry, ToAvailability(match)));
            }

            return items;
        }

        /// <inheritdoc />
        public async Task<ReleaseCalendarMonth?> GetLatestMonthAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            DateTime? latest = await scope.ServiceProvider.GetRequiredService<IReleaseCalendarDataService>()
                .GetLatestReleaseDateAsync(cancellationToken);

            return latest is null ? null : ReleaseCalendarMonth.FromUtc(latest.Value, _timeZone);
        }

        /// <summary>Ordnet das Ergebnis des Bestandsabgleichs einer Stufe zu.</summary>
        /// <param name="match">Das Ergebnis des Abgleichs.</param>
        /// <returns>Die Stufe.</returns>
        internal static ReleaseAvailability ToAvailability(LocalEpisodeMatch match)
        {
            if (match.HasLocalTrack)
            {
                return ReleaseAvailability.Local;
            }

            return match.EpisodeId != Guid.Empty ? ReleaseAvailability.InLibrary : ReleaseAvailability.Missing;
        }

        /// <summary>Baut die Kachel zu einem Eintrag.</summary>
        /// <param name="entry">Der Kalendereintrag samt Serie.</param>
        /// <param name="availability">Wo die Folge im Bestand liegt.</param>
        /// <returns>Die Kachel.</returns>
        internal ReleaseCalendarItemViewModel BuildItem(ReleaseCalendarEntry entry, ReleaseAvailability availability)
        {
            DateTime utc = DateTime.SpecifyKind(entry.ReleaseDate, DateTimeKind.Utc);
            DateTime localRelease = TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
            DateTime localToday = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _timeZone).Date;
            bool isAnnounced = localRelease.Date > localToday;

            // Monat und Tag nach der Oberflächensprache: „14. November" oder „November 14".
            string dateLine = localRelease.ToString("M", _culture);
            if (entry.EpisodeNumber.HasValue)
            {
                string numberFormat = _localizationService?.Get("NewEpisodeInfoNumberFormat") ?? "Nr. {0}";
                dateLine += " · " + string.Format(_culture, numberFormat, entry.EpisodeNumber.Value);
            }

            return new ReleaseCalendarItemViewModel(
                new ReleaseCalendarItemData
                {
                    SeriesId = entry.SeriesId,
                    SeriesName = entry.Series?.Title ?? string.Empty,
                    EpisodeTitle = entry.Title,
                    DateLine = dateLine,
                    CoverUri = ResolveCoverUri(entry),
                    BadgeText = ResolveBadge(isAnnounced, availability),
                    ReleaseDateUtc = utc,
                    IsAnnounced = isAnnounced,
                    Availability = availability,
                });
        }

        /// <summary>
        /// Wählt das Kennzeichen: Angekündigtes zuerst, dann ob die Folge schon im Bestand
        /// liegt. Eine erschienene Folge, die fehlt, braucht keines; die Kachel selbst sagt
        /// dann schon, dass sie neu ist.
        /// </summary>
        private string? ResolveBadge(bool isAnnounced, ReleaseAvailability availability)
        {
            if (isAnnounced)
            {
                return _localizationService?.Get("BadgeAnnounced") ?? "Angekündigt";
            }

            return availability switch
            {
                ReleaseAvailability.Local => _localizationService?.Get("ReleaseCalendarBadgeLocal") ?? "Vorhanden",
                ReleaseAvailability.InLibrary => _localizationService?.Get("ReleaseCalendarBadgeInLibrary") ?? "In der Mediathek",
                _ => null
            };
        }

        /// <summary>
        /// Das Album-Bild in 300 Pixeln, wenn der Anbieter eines liefert; sonst das Cover der
        /// Serie. Die Adresse des Anbieters enthält die Größe und lässt sich dafür umschreiben.
        /// </summary>
        private static Uri? ResolveCoverUri(ReleaseCalendarEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.CoverUrl))
            {
                return WebAddress.TryParse(entry.CoverUrl.Replace("100x100bb", "300x300bb", StringComparison.Ordinal));
            }

            return WebAddress.TryParse(entry.Series?.CoverImageUrl);
        }
    }
}
