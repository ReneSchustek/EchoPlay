using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Abstractions;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Abschnitt „Veröffentlichungen" der Startseite: was in einem Monat bei allen
    /// vorhandenen Serien erscheint, mit Umschalter auf Vor- und Folgemonate.
    /// </summary>
    /// <remarks>
    /// Zurück geht es bis zum Vormonat, weil der Kalender nicht weiter zurückreicht. Vor geht
    /// es mindestens bis zum Folgemonat und darüber hinaus bis zur spätesten Ankündigung.
    /// </remarks>
    public sealed class DashboardReleaseCalendarViewModel : ObservableObject, IDisposable
    {
        private readonly IReleaseCalendarSource _source;
        private readonly IClock _clock;
        private readonly TimeZoneInfo _timeZone;
        private readonly CultureInfo _culture;
        private readonly ILogger _logger;

        private ObservableCollection<ReleaseCalendarItemViewModel> _items = [];
        private ReleaseCalendarMonth? _selectedMonth;
        private ReleaseCalendarMonth _latestMonth;
        private string _monthLabel = string.Empty;
        private bool _isAvailable;
        private bool _isRefreshing;
        private bool _hasLoaded;

        // Jeder Monatswechsel bricht das Laden des vorigen ab, damit ein langsamer Monat
        // nicht nachträglich den gerade gewählten überschreibt.
        private CancellationTokenSource? _loadCts;

        /// <summary>
        /// Initialisiert den Abschnitt.
        /// </summary>
        /// <param name="source">Liefert die Kacheln eines Monats.</param>
        /// <param name="clock">Zeitquelle für den laufenden Monat.</param>
        /// <param name="timeZone">Zeitzone des Nutzers.</param>
        /// <param name="culture">Kultur der Oberfläche für den Monatsnamen.</param>
        /// <param name="logger">Protokollkanal.</param>
        internal DashboardReleaseCalendarViewModel(
            IReleaseCalendarSource source,
            IClock clock,
            TimeZoneInfo timeZone,
            CultureInfo culture,
            ILogger logger)
        {
            _source = source;
            _clock = clock;
            _timeZone = timeZone;
            _culture = culture;
            _logger = logger;

            PreviousMonthCommand = new RelayCommand(() => _ = ShiftMonthSafelyAsync(-1));
            NextMonthCommand = new RelayCommand(() => _ = ShiftMonthSafelyAsync(+1));
            PreviousMonthCommand.SetEnabled(false);
            NextMonthCommand.SetEnabled(false);
        }

        /// <summary>Die Kacheln des gewählten Monats.</summary>
        public ObservableCollection<ReleaseCalendarItemViewModel> Items
        {
            get => _items;
            private set
            {
                if (SetProperty(ref _items, value))
                {
                    RaiseVisibilityChanged();
                }
            }
        }

        /// <summary>Name des gewählten Monats mit Jahr, etwa „November 2026".</summary>
        public string MonthLabel
        {
            get => _monthLabel;
            private set => SetProperty(ref _monthLabel, value);
        }

        /// <summary>Wechselt auf den Vormonat.</summary>
        public RelayCommand PreviousMonthCommand { get; }

        /// <summary>Wechselt auf den Folgemonat.</summary>
        public RelayCommand NextMonthCommand { get; }

        /// <summary>Der gewählte Monat; vor dem ersten Laden leer.</summary>
        internal ReleaseCalendarMonth? SelectedMonth => _selectedMonth;

        /// <summary>
        /// Ob der Abschnitt überhaupt erscheint. Im Offline-Modus fragt niemand iTunes, der
        /// Kalender bliebe also auf altem Stand; dann fehlt er wie die Neuerscheinungen.
        /// </summary>
        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (SetProperty(ref _isAvailable, value))
                {
                    RaiseVisibilityChanged();
                }
            }
        }

        /// <summary>Ob der Kalender gerade im Hintergrund aufgebaut wird.</summary>
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                if (SetProperty(ref _isRefreshing, value))
                {
                    RaiseVisibilityChanged();
                }
            }
        }

        /// <summary>Sichtbarkeit des ganzen Abschnitts.</summary>
        public Visibility SectionVisibility => _isAvailable ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit der Kachelreihe.</summary>
        public Visibility ItemsVisibility => _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Hinweises, dass der Kalender noch aufgebaut wird.</summary>
        public Visibility RefreshingHintVisibility =>
            _items.Count == 0 && _isRefreshing ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Hinweises, dass im Monat nichts ansteht.</summary>
        public Visibility EmptyHintVisibility =>
            _items.Count == 0 && !_isRefreshing && _hasLoaded ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Lädt den gewählten Monat neu, beim ersten Aufruf den laufenden. Ein Monat, den der
        /// Nutzer schon gewählt hat, bleibt beim Nachladen stehen.
        /// </summary>
        /// <param name="cancellationToken">Bricht das Laden ab.</param>
        /// <returns>Der Task ist abgeschlossen, wenn Kacheln und Umschalter stehen.</returns>
        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            ReleaseCalendarMonth month = _selectedMonth ?? CurrentMonth;
            return ShowMonthAsync(month, cancellationToken);
        }

        /// <summary>Bricht ein laufendes Laden ab.</summary>
        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }

        /// <summary>Der Monat, in dem „heute" in der Zeitzone des Nutzers liegt.</summary>
        private ReleaseCalendarMonth CurrentMonth => ReleaseCalendarMonth.FromUtc(_clock.UtcNow, _timeZone);

        /// <summary>
        /// Zeigt einen Monat an. Liegt er außerhalb des Bereichs, wird er auf die Grenze gezogen.
        /// </summary>
        private async Task ShowMonthAsync(ReleaseCalendarMonth month, CancellationToken cancellationToken)
        {
            // Das verknüpfte Zeichen lebt nur, solange dieses Laden läuft. Bliebe es danach am
            // Zeichen des Aufrufers hängen, liefe dessen nächster Abbruch über einen Rückruf
            // im Thread-Pool statt sofort, und die Startseite lüde später nach als gewollt.
            using CancellationTokenSource own = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenSource? previous = _loadCts;
            _loadCts = own;
            if (previous is not null)
            {
                await previous.CancelAsync();
            }

            try
            {
                await ShowMonthCoreAsync(month, own.Token);
            }
            finally
            {
                if (ReferenceEquals(_loadCts, own))
                {
                    _loadCts = null;
                }
            }
        }

        /// <summary>Lädt und setzt den Monat; siehe <see cref="ShowMonthAsync"/>.</summary>
        private async Task ShowMonthCoreAsync(ReleaseCalendarMonth month, CancellationToken ct)
        {
            ReleaseCalendarMonth current = CurrentMonth;
            ReleaseCalendarMonth? latest = await _source.GetLatestMonthAsync(ct);
            ReleaseCalendarMonth earliest = current.Previous;
            _latestMonth = latest is { } known && known > current.Next ? known : current.Next;

            if (month < earliest)
            {
                month = earliest;
            }
            else if (month > _latestMonth)
            {
                month = _latestMonth;
            }

            IReadOnlyList<ReleaseCalendarItemViewModel> items = await _source.LoadMonthAsync(month, ct);
            ct.ThrowIfCancellationRequested();

            _selectedMonth = month;
            _hasLoaded = true;
            MonthLabel = FormatMonth(month);
            Items = new ObservableCollection<ReleaseCalendarItemViewModel>(items);
            PreviousMonthCommand.SetEnabled(month > earliest);
            NextMonthCommand.SetEnabled(month < _latestMonth);
            RaiseVisibilityChanged();
        }

        /// <summary>
        /// Wechselt um einen Monat. Fehler landen im Protokoll, statt als unbeobachtete
        /// Ausnahme aus dem Befehl zu verschwinden.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Befehl ohne Aufrufer: Ein Datenbankfehler beim Monatswechsel darf die Startseite nicht beenden; der Abschnitt bleibt auf dem alten Monat stehen.")]
        private async Task ShiftMonthSafelyAsync(int delta)
        {
            ReleaseCalendarMonth from = _selectedMonth ?? CurrentMonth;
            ReleaseCalendarMonth target = delta < 0 ? from.Previous : from.Next;

            try
            {
                await ShowMonthAsync(target, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Ein schnellerer Monatswechsel hat diesen abgelöst.
            }
            catch (Exception ex)
            {
                _logger.Warning("Monatswechsel im Veröffentlichungskalender fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>Monatsname in der Oberflächensprache und Jahr.</summary>
        private string FormatMonth(ReleaseCalendarMonth month)
            => $"{_culture.DateTimeFormat.GetMonthName(month.Month)} {month.Year.ToString(CultureInfo.InvariantCulture)}";

        private void RaiseVisibilityChanged()
        {
            OnPropertyChanged(nameof(SectionVisibility));
            OnPropertyChanged(nameof(ItemsVisibility));
            OnPropertyChanged(nameof(RefreshingHintVisibility));
            OnPropertyChanged(nameof(EmptyHintVisibility));
        }
    }
}
