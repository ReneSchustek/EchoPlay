using EchoPlay.App.Infrastructure;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Zeigt einen einzelnen Log-Eintrag in der Protokoll-Ansicht.
    /// Der Level bestimmt die Farbe in der UI – darüber hinaus wird er als Text angezeigt.
    /// </summary>
    /// <param name="Timestamp">Formatierter Zeitstempel, z.B. "14:32:07".</param>
    /// <param name="Level">Wichtigkeitsstufe des Eintrags.</param>
    /// <param name="Category">Quelle des Logs, z.B. "SyncService".</param>
    /// <param name="Message">Die eigentliche Nachricht.</param>
    public sealed record LogEntryViewModel(
        string Timestamp,
        LogLevel Level,
        string Category,
        string Message);

    /// <summary>
    /// ViewModel für die Protokoll-Seite.
    /// Abonniert <see cref="ILiveLogSink.LogEntryAdded"/> und leitet neue Einträge
    /// per <c>DispatcherQueue.TryEnqueue</c> auf den UI-Thread weiter.
    ///
    /// Das Live-Update kann per <see cref="IsLiveActive"/> pausiert werden – beim
    /// Reaktivieren werden aktuelle Einträge aus dem Puffer neu geladen.
    ///
    /// Die Liste ist auf <see cref="MaxLiveEntries"/> Einträge begrenzt, um bei
    /// langer Laufzeit keinen Speicherüberlauf zu verursachen.
    /// </summary>
    public sealed class LogViewModel : ObservableObject, IDisposable
    {
        /// <summary>Maximale Anzahl sichtbarer Einträge im Live-Modus.</summary>
        private const int MaxLiveEntries = 500;

        private readonly MemorySink? _memorySink;

        // Vollständiger Bestand hinter der angezeigten Liste. Ohne ihn wäre eine
        // zurückgenommene Suche nicht mehr aufzufüllen — die verworfenen Einträge sind weg,
        // sobald sie aus dem Puffer des Loggers gelaufen sind.
        private readonly List<LogEntryViewModel> _allEntries = [];

        private readonly LogEntryFilter _filter = new();

        private DispatcherQueue? _dispatcherQueue;
        private bool _isLiveActive = true;
        private bool _disposed;

        /// <summary>
        /// Initialisiert das ViewModel.
        /// </summary>
        /// <param name="memorySink">
        /// Optionaler In-Memory-Puffer. Ist er <see langword="null"/>, bleibt die Protokoll-Liste leer.
        /// </param>
        public LogViewModel(MemorySink? memorySink = null)
        {
            _memorySink = memorySink;
            LogEntries = [];

            ToggleLiveCommand = new RelayCommand(ToggleLive);
            ClearCommand = new RelayCommand(ClearEntries);
            ResetFiltersCommand = new RelayCommand(ResetFilters);
        }

        /// <summary>
        /// Einträge in der Protokoll-Ansicht – neueste zuerst.
        /// </summary>
        public ObservableCollection<LogEntryViewModel> LogEntries { get; }

        /// <summary>
        /// Steuert, ob neue Einträge automatisch erscheinen.
        /// Beim Einschalten werden zunächst alle gepufferten Einträge geladen.
        /// </summary>
        public bool IsLiveActive
        {
            get => _isLiveActive;
            private set => SetProperty(ref _isLiveActive, value);
        }

        /// <summary>Schaltet das Live-Update um.</summary>
        public ICommand ToggleLiveCommand { get; }

        /// <summary>Leert die angezeigte Liste – der MemorySink-Puffer bleibt unverändert.</summary>
        public ICommand ClearCommand { get; }

        /// <summary>Nimmt Suche und Stufenfilter zurück.</summary>
        public ICommand ResetFiltersCommand { get; }

        /// <summary>
        /// Freitextsuche über den Meldungstext und die Kategorie. Wirkt beim Tippen.
        /// </summary>
        public string SearchText
        {
            get => _filter.SearchText;
            set
            {
                if (string.Equals(_filter.SearchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _filter.SearchText = value;
                OnPropertyChanged();
                RebuildVisibleEntries();
            }
        }

        /// <summary>
        /// Gewählte Stufe oder <see langword="null"/> für alle Stufen.
        /// <para>
        /// Die Stufe filtert genau, nicht ab einer Schwelle: Wer nach Warnungen sucht, will
        /// Warnungen sehen und nicht zusätzlich jeden Fehler.
        /// </para>
        /// </summary>
        public LogLevel? LevelFilter
        {
            get => _filter.Level;
            set
            {
                if (_filter.Level == value)
                {
                    return;
                }

                _filter.Level = value;
                OnPropertyChanged();
                RebuildVisibleEntries();
            }
        }

        /// <summary>Ob Suche oder Stufenfilter die Anzeige gerade einschränken.</summary>
        public bool HasActiveFilter => _filter.IsActive;

        /// <summary>
        /// Sichtbarkeit des „Nichts gefunden"-Hinweises – es liegen Meldungen vor, aber Suche
        /// oder Stufe lassen keine übrig.
        /// </summary>
        public Visibility NoResultsVisibility =>
            _allEntries.Count > 0 && LogEntries.Count == 0 && HasActiveFilter
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// Wird von der Seite in <c>OnNavigatedTo</c> aufgerufen.
        /// Übergibt den UI-Thread-Dispatcher und lädt die aktuellen Einträge,
        /// sofern der Live-Modus aktiv ist.
        /// </summary>
        /// <param name="queue">
        /// DispatcherQueue des UI-Threads. Ohne sie werden die gepufferten Meldungen trotzdem
        /// geladen — nur live eintreffende bleiben aus, weil sie keinen Weg auf den UI-Thread
        /// hätten.
        /// </param>
        public void Activate(DispatcherQueue? queue)
        {
            _dispatcherQueue = queue;

            if (_memorySink is null)
            {
                return;
            }

            if (_isLiveActive)
            {
                LoadCurrentEntries();
                _memorySink.LogEntryAdded += OnNewEntry;
            }
        }

        /// <summary>
        /// Wird von der Seite in <c>OnNavigatedFrom</c> aufgerufen.
        /// Hebt das Event-Abonnement auf, damit das ViewModel korrekt freigegeben werden kann.
        /// </summary>
        public void Deactivate()
        {
            if (_memorySink is not null)
            {
                _memorySink.LogEntryAdded -= OnNewEntry;
            }

            _dispatcherQueue = null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Deactivate();
            _disposed = true;
        }

        /// <summary>
        /// Schaltet zwischen Live- und Pause-Modus um.
        /// Beim Einschalten: Einträge neu laden und Abo starten.
        /// Beim Ausschalten: Abo beenden.
        /// </summary>
        private void ToggleLive()
        {
            if (_memorySink is null)
            {
                return;
            }

            if (_isLiveActive)
            {
                _memorySink.LogEntryAdded -= OnNewEntry;
                IsLiveActive = false;
            }
            else
            {
                // Erst abonnieren, dann laden – keine Einträge können verloren gehen
                _memorySink.LogEntryAdded += OnNewEntry;
                LoadCurrentEntries();
                IsLiveActive = true;
            }
        }

        /// <summary>
        /// Lädt alle aktuell gepufferten Einträge aus dem MemorySink und zeigt sie an.
        /// Neueste Einträge landen oben – daher wird die älteste Reihenfolge umgekehrt.
        /// </summary>
        private void LoadCurrentEntries()
        {
            _allEntries.Clear();

            if (_memorySink is null)
            {
                RebuildVisibleEntries();
                return;
            }

            IReadOnlyList<LogEntry> buffered = _memorySink.GetEntries();

            // MemorySink liefert älteste zuerst – wir zeigen neueste oben
            for (int i = buffered.Count - 1; i >= 0; i--)
            {
                _allEntries.Add(Map(buffered[i]));
            }

            RebuildVisibleEntries();
        }

        /// <summary>Leert die Anzeige samt Bestand; der Puffer des Loggers bleibt unberührt.</summary>
        private void ClearEntries()
        {
            _allEntries.Clear();
            RebuildVisibleEntries();
        }

        /// <summary>Nimmt Suche und Stufenfilter zurück und zeigt wieder alle Meldungen.</summary>
        private void ResetFilters()
        {
            _filter.Reset();

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(LevelFilter));

            RebuildVisibleEntries();
        }

        /// <summary>
        /// Baut die angezeigte Liste aus dem Bestand neu auf. Wird nur bei Filterwechseln
        /// gebraucht — ein einzelner neuer Eintrag wird gezielt eingefügt, damit ein laufendes
        /// Protokoll nicht bei jeder Meldung die ganze Liste neu aufbaut.
        /// </summary>
        private void RebuildVisibleEntries()
        {
            LogEntries.Clear();

            foreach (LogEntryViewModel entry in _allEntries)
            {
                if (_filter.Matches(entry))
                {
                    LogEntries.Add(entry);
                }
            }

            OnPropertyChanged(nameof(HasActiveFilter));
            OnPropertyChanged(nameof(NoResultsVisibility));
        }

        /// <summary>
        /// Empfängt einen neuen Eintrag vom Logger-Thread und fügt ihn via
        /// <c>DispatcherQueue.TryEnqueue</c> sicher auf dem UI-Thread ein.
        /// </summary>
        /// <param name="entry">Der neue Log-Eintrag.</param>
        private void OnNewEntry(LogEntry entry)
        {
            if (_dispatcherQueue is null)
            {
                return;
            }

            _ = _dispatcherQueue.TryEnqueue(() =>
            {
                LogEntryViewModel mapped = Map(entry);
                _allEntries.Insert(0, mapped);

                if (_filter.Matches(mapped))
                {
                    LogEntries.Insert(0, mapped);
                }

                // Limit einhalten – ältester Eintrag ist immer am Ende der Liste
                if (_allEntries.Count > MaxLiveEntries)
                {
                    LogEntryViewModel dropped = _allEntries[^1];
                    _allEntries.RemoveAt(_allEntries.Count - 1);
                    _ = LogEntries.Remove(dropped);
                }

                OnPropertyChanged(nameof(NoResultsVisibility));
            });
        }

        /// <summary>
        /// Wandelt einen <see cref="LogEntry"/> in ein für die UI optimiertes ViewModel um.
        /// Der Zeitstempel wird auf Stunden:Minuten:Sekunden gekürzt – das Datum ist für ein
        /// Live-Protokoll irrelevant.
        /// </summary>
        private static LogEntryViewModel Map(LogEntry entry) =>
            // LogEntry.Timestamp ist UTC; Lokalzeit für die Live-Anzeige.
            new(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                entry.Level,
                entry.Category,
                entry.Message);
    }
}
