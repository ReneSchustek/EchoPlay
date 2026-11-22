using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Seite „Angekündigt": alle Folgen der vorhandenen Serien, die noch nicht erschienen sind,
    /// nach Monat gruppiert und innerhalb des Monats nach Erscheinungstag.
    /// </summary>
    /// <remarks>
    /// Quelle ist der Veröffentlichungskalender, nicht der Zwischenspeicher der
    /// Neuerscheinungen: Der Kalender gilt für alle Serien, die Neuerscheinungen nur für
    /// überwachte.
    /// </remarks>
    public sealed class AnnouncedViewModel : ObservableObject
    {
        private readonly IReleaseCalendarSource _source;
        private readonly TimeZoneInfo _timeZone;
        private readonly CultureInfo _culture;

        private ObservableCollection<AnnouncedMonthGroupViewModel> _groups = [];
        private bool _isLoading;
        private bool _hasLoaded;

        /// <summary>
        /// Initialisiert die Seite mit den Diensten aus dem Container.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Abfrage einen Bereich.</param>
        /// <param name="clock">Zeitquelle für die Abgrenzung „angekündigt".</param>
        /// <param name="localizationService">Liefert die Anzeigetexte der Kacheln.</param>
        public AnnouncedViewModel(IServiceScopeFactory scopeFactory, IClock clock, ILocalizationService localizationService)
            : this(CreateSource(scopeFactory, clock, localizationService), TimeZoneInfo.Local, NewReleaseGroupBuilder.ResolveUiCulture())
        {
        }

        /// <summary>
        /// Initialisiert die Seite mit einer eigenen Quelle.
        /// </summary>
        /// <param name="source">Liefert die angekündigten Folgen.</param>
        /// <param name="timeZone">Zeitzone für die Monatsgrenzen.</param>
        /// <param name="culture">Kultur der Oberfläche für den Monatsnamen.</param>
        internal AnnouncedViewModel(IReleaseCalendarSource source, TimeZoneInfo timeZone, CultureInfo culture)
        {
            _source = source;
            _timeZone = timeZone;
            _culture = culture;
        }

        /// <summary>Die Monate mit ihren Kacheln, der früheste zuerst.</summary>
        public ObservableCollection<AnnouncedMonthGroupViewModel> Groups
        {
            get => _groups;
            private set
            {
                if (SetProperty(ref _groups, value))
                {
                    RaiseVisibilityChanged();
                }
            }
        }

        /// <summary>Ob die Liste gerade geladen wird.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaiseVisibilityChanged();
                }
            }
        }

        /// <summary>Sichtbarkeit der Ladeanzeige.</summary>
        public Visibility LoadingVisibility => _isLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Hinweises, dass nichts angekündigt ist.</summary>
        public Visibility EmptyHintVisibility =>
            _hasLoaded && !_isLoading && _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Lädt die angekündigten Folgen neu.
        /// </summary>
        /// <param name="cancellationToken">Bricht das Laden ab.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Gruppen stehen.</returns>
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            IsLoading = true;
            try
            {
                IReadOnlyList<ReleaseCalendarItemViewModel> items = await _source.LoadAnnouncedAsync(cancellationToken);
                Groups = new ObservableCollection<AnnouncedMonthGroupViewModel>(GroupByMonth(items));
                _hasLoaded = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Gruppiert die Kacheln nach dem Monat ihres Erscheinens in der Zeitzone des Nutzers.
        /// Die Quelle liefert sie schon nach Tag sortiert; die Reihenfolge bleibt erhalten.
        /// </summary>
        /// <param name="items">Die Kacheln, nach Erscheinungszeitpunkt aufsteigend.</param>
        /// <returns>Die Gruppen, der früheste Monat zuerst.</returns>
        internal IEnumerable<AnnouncedMonthGroupViewModel> GroupByMonth(IReadOnlyList<ReleaseCalendarItemViewModel> items)
            => items
                .GroupBy(item => ReleaseCalendarMonth.FromUtc(item.ReleaseDateUtc, _timeZone))
                .Select(group => new AnnouncedMonthGroupViewModel(FormatMonth(group.Key), group.ToArray()));

        /// <summary>Monatsname in der Oberflächensprache und Jahr.</summary>
        private string FormatMonth(ReleaseCalendarMonth month)
            => $"{_culture.DateTimeFormat.GetMonthName(month.Month)} {month.Year.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>Baut die Quelle mit Zeitzone und Oberflächensprache des Rechners.</summary>
        private static ReleaseCalendarSource CreateSource(
            IServiceScopeFactory scopeFactory, IClock clock, ILocalizationService localizationService)
            => new(scopeFactory, clock, TimeZoneInfo.Local, NewReleaseGroupBuilder.ResolveUiCulture(), localizationService);

        private void RaiseVisibilityChanged()
        {
            OnPropertyChanged(nameof(LoadingVisibility));
            OnPropertyChanged(nameof(EmptyHintVisibility));
        }
    }
}
