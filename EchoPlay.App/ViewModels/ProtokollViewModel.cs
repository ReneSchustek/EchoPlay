using EchoPlay.App.Infrastructure;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using Microsoft.UI.Dispatching;
using System;
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
    public sealed class ProtokollViewModel : ObservableObject, IDisposable
    {
        /// <summary>Maximale Anzahl sichtbarer Einträge im Live-Modus.</summary>
        private const int MaxLiveEntries = 500;

        private readonly MemorySink? _memorySink;
        private DispatcherQueue? _dispatcherQueue;
        private bool _isLiveActive = true;
        private bool _disposed;

        /// <summary>
        /// Initialisiert das ViewModel.
        /// </summary>
        /// <param name="memorySink">
        /// Optionaler In-Memory-Puffer. Ist er <see langword="null"/>, bleibt die Protokoll-Liste leer.
        /// </param>
        public ProtokollViewModel(MemorySink? memorySink = null)
        {
            _memorySink = memorySink;
            LogEntries  = [];

            ToggleLiveCommand = new RelayCommand(ToggleLive);
            ClearCommand      = new RelayCommand(() => LogEntries.Clear());
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

        /// <summary>
        /// Wird von der Seite in <c>OnNavigatedTo</c> aufgerufen.
        /// Übergibt den UI-Thread-Dispatcher und lädt die aktuellen Einträge,
        /// sofern der Live-Modus aktiv ist.
        /// </summary>
        /// <param name="queue">DispatcherQueue des UI-Threads.</param>
        public void Activate(DispatcherQueue queue)
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
            LogEntries.Clear();

            if (_memorySink is null)
            {
                return;
            }

            IReadOnlyList<LogEntry> buffered = _memorySink.GetEntries();

            // MemorySink liefert älteste zuerst – wir zeigen neueste oben
            for (int i = buffered.Count - 1; i >= 0; i--)
            {
                LogEntries.Add(Map(buffered[i]));
            }
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
                LogEntries.Insert(0, Map(entry));

                // Limit einhalten – ältester Eintrag ist immer am Ende der Liste
                if (LogEntries.Count > MaxLiveEntries)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            });
        }

        /// <summary>
        /// Wandelt einen <see cref="LogEntry"/> in ein für die UI optimiertes ViewModel um.
        /// Der Zeitstempel wird auf Stunden:Minuten:Sekunden gekürzt – das Datum ist für ein
        /// Live-Protokoll irrelevant.
        /// </summary>
        private static LogEntryViewModel Map(LogEntry entry) =>
            new(entry.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                entry.Level,
                entry.Category,
                entry.Message);
    }
}
