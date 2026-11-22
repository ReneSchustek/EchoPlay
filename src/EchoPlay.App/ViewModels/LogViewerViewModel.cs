using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Logger.Models;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Protokolle-Tab der Einstellungsseite: Auswahl der Protokolldatei,
    /// Live-Puffer und die Filter nach Text und Schweregrad. Trägt keinen gespeicherten
    /// Einstellungswert.
    /// </summary>
    public sealed class LogViewerViewModel : ObservableObject, IDisposable
    {
        private readonly ILogViewerCoordinator _logViewerCoordinator;

        // Leer im Testprozess; dann läuft der Neuaufbau direkt auf dem meldenden Thread.
        private readonly DispatcherQueue? _dispatcherQueue;

        // 1, solange ein Neuaufbau der Live-Ansicht auf dem Oberflächen-Thread aussteht.
        private int _liveRefreshPending;

        private IReadOnlyList<LogFileOption> _availableLogFiles = [];
        private LogFileOption? _selectedLogFile;
        private string _logSearchText = string.Empty;
        private LogLevel _logMinimumLevel = LogLevel.Debug;
        private bool _isLiveViewActive;

        /// <summary>
        /// Initialisiert die Protokollansicht.
        /// </summary>
        /// <param name="logViewerCoordinator">App-Service für Datei- und Live-Log-Zugriff.</param>
        /// <param name="dispatcherQueue">Warteschlange der Oberfläche für die Live-Ansicht; <see langword="null"/> in Tests.</param>
        public LogViewerViewModel(ILogViewerCoordinator logViewerCoordinator, DispatcherQueue? dispatcherQueue = null)
        {
            _logViewerCoordinator = logViewerCoordinator;
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>Gefilterte und formatierte Log-Einträge für den Log-Viewer.</summary>
        public ObservableCollection<string> LogEntries { get; } = [];

        /// <summary>
        /// Gibt an, ob der Log-Viewer überhaupt verfügbar ist (MemorySink beim Start registriert).
        /// </summary>
        public bool IsLogViewerAvailable => _logViewerCoordinator.IsLiveViewAvailable;

        /// <summary>Freitext-Suchfilter für den Log-Viewer. Jede Änderung triggert ein Refresh.</summary>
        public string LogSearchText
        {
            get => _logSearchText;
            set
            {
                if (SetProperty(ref _logSearchText, value))
                {
                    RefreshLogs();
                }
            }
        }

        /// <summary>Minimales Log-Level für den Log-Viewer-Filter. Jede Änderung triggert ein Refresh.</summary>
        public LogLevel LogMinimumLevel
        {
            get => _logMinimumLevel;
            set
            {
                if (SetProperty(ref _logMinimumLevel, value))
                {
                    RefreshLogs();
                }
            }
        }

        /// <summary>
        /// Index des Level-Filters in der ComboBox (0=Alle, 1=Info+, 2=Warnung+, 3=Fehler+).
        /// </summary>
        public int LogLevelFilterIndex
        {
            get => _logMinimumLevel switch
            {
                LogLevel.Debug => 0,
                LogLevel.Information => 1,
                LogLevel.Warning => 2,
                _ => 3
            };
            set => LogMinimumLevel = value switch
            {
                1 => LogLevel.Information,
                2 => LogLevel.Warning,
                3 => LogLevel.Error,
                _ => LogLevel.Debug
            };
        }

        /// <summary>
        /// Liste aller verfügbaren Log-Dateien. An erster Stelle steht immer „Aktuell (Live)".
        /// </summary>
        public IReadOnlyList<LogFileOption> AvailableLogFiles
        {
            get => _availableLogFiles;
            private set => SetProperty(ref _availableLogFiles, value);
        }

        /// <summary>Aktuell gewählte Log-Datei. <see langword="null"/>-FilePath bedeutet Live-Modus.</summary>
        public LogFileOption? SelectedLogFile
        {
            get => _selectedLogFile;
            set
            {
                if (SetProperty(ref _selectedLogFile, value))
                {
                    _ = LoadLogContentAsync(value);
                }
            }
        }

        /// <summary>
        /// Gibt an, ob die Protokollansicht jeden neuen Eintrag sofort zeigt.
        /// </summary>
        public bool IsLiveViewActive
        {
            get => _isLiveViewActive;
            set
            {
                if (!SetProperty(ref _isLiveViewActive, value))
                {
                    return;
                }

                if (value)
                {
                    _logViewerCoordinator.LiveEntriesChanged += OnLiveEntriesChanged;

                    // Was seit dem letzten Blick dazugekommen ist, sofort zeigen, nicht erst
                    // mit dem nächsten Eintrag.
                    RefreshLiveViewIfSelected();
                }
                else
                {
                    _logViewerCoordinator.LiveEntriesChanged -= OnLiveEntriesChanged;
                }
            }
        }

        /// <summary>
        /// Lädt die Liste der verfügbaren Log-Dateien neu. Der erste Eintrag ist immer die Live-Option.
        /// Die zuvor gewählte Datei wird beibehalten, sofern sie noch existiert; andernfalls
        /// fällt die Auswahl auf die Live-Option zurück.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Liste steht und eine Auswahl gesetzt ist.</returns>
        public async Task LoadLogFilesAsync()
        {
            IReadOnlyList<LogFileOption> options = await _logViewerCoordinator.LoadLogFileOptionsAsync();
            AvailableLogFiles = options;

            // Nur beim ersten Laden auf Live setzen, danach Auswahl des Nutzers beibehalten
            if (_selectedLogFile is null && options.Count > 0)
            {
                _selectedLogFile = options[0];
                OnPropertyChanged(nameof(SelectedLogFile));
            }
        }

        /// <summary>
        /// Liest alle gepufferten Einträge aus dem MemorySink und wendet Such- und Level-Filter an.
        /// Neueste Einträge stehen am Ende der Liste.
        /// </summary>
        public void RefreshLogs()
        {
            ReplaceEntries(_logViewerCoordinator.BuildFilteredLiveEntries(_logSearchText, _logMinimumLevel));
        }

        /// <summary>
        /// Meldet die Live-Ansicht ab. Wird von <see cref="SettingsViewModel.Dispose"/> beim
        /// Verlassen der Seite aufgerufen; der Coordinator lebt weiter und hielte das
        /// Ansichtsmodell sonst über sein Ereignis fest.
        /// </summary>
        public void Dispose()
        {
            _logViewerCoordinator.LiveEntriesChanged -= OnLiveEntriesChanged;
        }

        /// <summary>
        /// Lädt den Inhalt der gewählten Log-Datei in <see cref="LogEntries"/>.
        /// Die Live-Option (FilePath <see langword="null"/>) zeigt den MemorySink-Puffer.
        /// </summary>
        /// <param name="option">Die gewählte Option.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Einträge in der Anzeige stehen.</returns>
        private async Task LoadLogContentAsync(LogFileOption? option)
        {
            if (option?.FilePath is null)
            {
                // Live-Modus: aus dem MemorySink-Puffer laden
                RefreshLogs();
                return;
            }

            LogEntries.Clear();
            ReplaceEntries(await _logViewerCoordinator.LoadFileLinesAsync(option.FilePath));
        }

        private void ReplaceEntries(IReadOnlyList<string> lines)
        {
            LogEntries.Clear();
            foreach (string line in lines)
            {
                LogEntries.Add(line);
            }
        }

        /// <summary>
        /// Kommt auf dem Thread an, der protokolliert hat. Ein Schub von Einträgen soll die
        /// Anzeige einmal neu aufbauen, nicht einmal je Eintrag: Solange ein Neuaufbau aussteht,
        /// deckt er auch die nachfolgenden Einträge ab.
        /// </summary>
        private void OnLiveEntriesChanged(object? sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _liveRefreshPending, 1) == 1)
            {
                return;
            }

            if (_dispatcherQueue is null)
            {
                RefreshLiveViewAfterNotification();
                return;
            }

            _ = _dispatcherQueue.TryEnqueue(RefreshLiveViewAfterNotification);
        }

        private void RefreshLiveViewAfterNotification()
        {
            // Erst freigeben, dann lesen: Ein Eintrag, der während des Lesens hinzukommt, stößt
            // so einen weiteren Neuaufbau an, statt verloren zu gehen.
            Volatile.Write(ref _liveRefreshPending, 0);
            RefreshLiveViewIfSelected();
        }

        /// <summary>
        /// Liest den Live-Puffer neu, sofern die Live-Option gewählt ist; eine gewählte Datei
        /// ändert sich nicht.
        /// </summary>
        private void RefreshLiveViewIfSelected()
        {
            if (_selectedLogFile?.FilePath is null)
            {
                RefreshLogs();
            }
        }
    }
}
