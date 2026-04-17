using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Verwaltung- und Protokolle-Tab der Einstellungsseite.
    /// Vereint Datenbankpflege (Cache leeren, Purge, VACUUM, Reset) und den Log-Viewer
    /// (Datei-Auswahl, Live-Puffer, Filter), weil beide Bereiche operativ zusammengehören
    /// und keinen eigenen Settings-Zustand mehr tragen müssen – die Entität speichert nur
    /// <see cref="DbPurgeDays"/> und <see cref="ClearCacheOnNextStart"/>.
    /// </summary>
    public sealed class MaintenanceSettingsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogViewerCoordinator _logViewerCoordinator;
        private readonly Action _onUserEdit;

        // DispatcherTimer für Live-View – aktualisiert den Log-Puffer alle 2 Sekunden
        private DispatcherTimer? _liveViewTimer;

        private bool _isBatchLoading;
        private int _dbPurgeDays = 30;
        private bool _clearCacheOnNextStart;
        private bool _dbBackupEnabled = true;
        private int _dbBackupRetentionCount = 5;
        private bool _isMaintaining;
        private string _maintenanceStatusText = string.Empty;

        private IReadOnlyList<LogFileOption> _availableLogFiles = [];
        private LogFileOption? _selectedLogFile;
        private string _logSearchText = string.Empty;
        private LogLevel _logMinimumLevel = LogLevel.Debug;
        private bool _isLiveViewActive;

        /// <summary>
        /// Initialisiert das Sub-VM mit DI-Scope-Fabrik, Log-Viewer-Coordinator und Edit-Callback.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankpflege-Services.</param>
        /// <param name="logViewerCoordinator">App-Service für Datei- und Live-Log-Zugriff.</param>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public MaintenanceSettingsViewModel(
            IServiceScopeFactory scopeFactory,
            ILogViewerCoordinator logViewerCoordinator,
            Action onUserEdit)
        {
            _scopeFactory = scopeFactory;
            _logViewerCoordinator = logViewerCoordinator;
            _onUserEdit = onUserEdit;
            LogEntries = [];
        }

        // ── Datenbankpflege-Eigenschaften ───────────────────────────────────────

        /// <summary>
        /// Anzahl der Tage nach denen soft-gelöschte Einträge physisch entfernt werden.
        /// 0 bedeutet sofortige Bereinigung.
        /// </summary>
        public int DbPurgeDays
        {
            get => _dbPurgeDays;
            set
            {
                if (SetProperty(ref _dbPurgeDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Wenn aktiviert, wird der Neuerscheinungen-Cache beim nächsten App-Start geleert.
        /// </summary>
        public bool ClearCacheOnNextStart
        {
            get => _clearCacheOnNextStart;
            set
            {
                if (SetProperty(ref _clearCacheOnNextStart, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Legt fest, ob der <see cref="EchoPlay.Data.Infrastructure.DatabaseInitializer"/> vor jeder Migration
        /// einen Snapshot der Datenbank anlegt.
        /// </summary>
        public bool DbBackupEnabled
        {
            get => _dbBackupEnabled;
            set
            {
                if (SetProperty(ref _dbBackupEnabled, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Anzahl der DB-Backups, die vor Migrationen vorgehalten werden.
        /// Gültiger Bereich 1–20. Der Wert 0 ist äquivalent zum Opt-Out.
        /// </summary>
        public int DbBackupRetentionCount
        {
            get => _dbBackupRetentionCount;
            set
            {
                if (SetProperty(ref _dbBackupRetentionCount, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Datenbankpflege-Vorgang läuft.</summary>
        public bool IsMaintaining
        {
            get => _isMaintaining;
            private set
            {
                if (SetProperty(ref _isMaintaining, value))
                {
                    OnPropertyChanged(nameof(IsNotMaintaining));
                }
            }
        }

        /// <summary>
        /// Invertierter Wartungszustand für Button-IsEnabled-Bindings.
        /// WinUI 3 hat keinen eingebauten BoolNegation-Converter – dieses Property ersetzt ihn.
        /// </summary>
        public bool IsNotMaintaining => !_isMaintaining;

        /// <summary>Statustext des letzten oder laufenden Wartungsvorgangs.</summary>
        public string MaintenanceStatusText
        {
            get => _maintenanceStatusText;
            private set
            {
                if (SetProperty(ref _maintenanceStatusText, value))
                {
                    OnPropertyChanged(nameof(MaintenanceStatusVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Wartungs-Statustexts.</summary>
        public Visibility MaintenanceStatusVisibility =>
            string.IsNullOrEmpty(_maintenanceStatusText) ? Visibility.Collapsed : Visibility.Visible;

        // ── Log-Viewer-Eigenschaften ────────────────────────────────────────────

        /// <summary>Gefilterte und formatierte Log-Einträge für den Log-Viewer.</summary>
        public ObservableCollection<string> LogEntries { get; }

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
        /// Gibt an, ob der Log-Viewer alle 2 Sekunden automatisch aktualisiert wird.
        /// Startet oder stoppt den internen Dispatcher-Timer entsprechend.
        /// </summary>
        public bool IsLiveViewActive
        {
            get => _isLiveViewActive;
            set
            {
                if (SetProperty(ref _isLiveViewActive, value))
                {
                    if (value)
                    {
                        StartLiveViewTimer();
                    }
                    else
                    {
                        _liveViewTimer?.Stop();
                    }
                }
            }
        }

        // ── Log-Viewer-Aktionen ─────────────────────────────────────────────────

        /// <summary>
        /// Lädt die Liste der verfügbaren Log-Dateien neu. Der erste Eintrag ist immer die Live-Option.
        /// Die zuvor gewählte Datei wird beibehalten, sofern sie noch existiert; andernfalls
        /// fällt die Auswahl auf die Live-Option zurück.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadLogFilesAsync()
        {
            IReadOnlyList<LogFileOption> options = await _logViewerCoordinator.LoadLogFileOptionsAsync();
            AvailableLogFiles = options;

            // Nur beim ersten Laden auf Live setzen – danach Auswahl des Nutzers beibehalten
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
            LogEntries.Clear();

            IReadOnlyList<string> lines = _logViewerCoordinator.BuildFilteredLiveEntries(_logSearchText, _logMinimumLevel);
            foreach (string line in lines)
            {
                LogEntries.Add(line);
            }
        }

        /// <summary>
        /// Lädt den Inhalt der gewählten Log-Datei in <see cref="LogEntries"/>.
        /// Die Live-Option (FilePath <see langword="null"/>) zeigt den MemorySink-Puffer.
        /// </summary>
        /// <param name="option">Die gewählte Option.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        private async Task LoadLogContentAsync(LogFileOption? option)
        {
            if (option?.FilePath is null)
            {
                // Live-Modus: aus dem MemorySink-Puffer laden
                RefreshLogs();
                return;
            }

            LogEntries.Clear();

            IReadOnlyList<string> lines = await _logViewerCoordinator.LoadFileLinesAsync(option.FilePath);
            foreach (string line in lines)
            {
                LogEntries.Add(line);
            }
        }

        /// <summary>
        /// Erstellt beim ersten Aufruf den <see cref="DispatcherTimer"/> und startet ihn.
        /// Der Timer muss im UI-Thread erstellt werden – das ViewModel wird immer dort instanziiert.
        /// </summary>
        private void StartLiveViewTimer()
        {
            if (_liveViewTimer is null)
            {
                _liveViewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _liveViewTimer.Tick += OnLiveViewTimerTick;
            }

            _liveViewTimer.Start();
        }

        /// <summary>
        /// Tick-Handler – lädt den MemorySink-Puffer neu.
        /// Ist eine Datei gewählt, passiert nichts – historische Logs ändern sich nicht.
        /// </summary>
        private void OnLiveViewTimerTick(object? sender, object e)
        {
            // Nur im Live-Modus aktualisieren – Datei-Logs sind statisch
            if (_selectedLogFile?.FilePath is null)
            {
                RefreshLogs();
            }
        }

        // ── Datenbankpflege-Aktionen ────────────────────────────────────────────

        /// <summary>
        /// Bereinigt soft-gelöschte Einträge die älter als <see cref="DbPurgeDays"/> Tage sind
        /// und kompaktiert anschließend die SQLite-Datei mit VACUUM.
        /// Läuft bereits eine Wartung, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "DB-Wartung (Purge/VACUUM): SQLite-Locks, Migration-Fehler oder IO-Fehler waehrend Vacuum werden als Nutzer-Status angezeigt; der Command darf nicht reissen, das IsMaintaining-Flag wird im finally zurueckgesetzt.")]
        public async Task RunMaintenanceAsync()
        {
            if (_isMaintaining)
            {
                return;
            }

            IsMaintaining = true;
            MaintenanceStatusText = "Bereinigung läuft …";

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                await maintenance.PurgeAsync(Math.Max(0, DbPurgeDays));
                await maintenance.VacuumAsync();

                MaintenanceStatusText = "Datenbank erfolgreich bereinigt.";
            }
            catch (Exception ex)
            {
                MaintenanceStatusText = $"Fehler bei der Bereinigung: {ex.Message}";
            }
            finally
            {
                IsMaintaining = false;
            }
        }

        /// <summary>
        /// Setzt die Bibliothek je nach Scope zurück.
        /// 0 = Online (nur online-importierte Serien), 1 = Lokal (nur lokale Verknüpfungen),
        /// 2 = Alle (kompletter Reset).
        /// </summary>
        /// <param name="scopeIndex">0 = Online, 1 = Lokal, 2 = Alle.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bibliothek-Reset: bulk-delete aller Serien/Episoden/Tracks und Cover-Dateien kann IO-/DB-Fehler werfen; der Command darf nicht reissen, der Status wird angezeigt und IsMaintaining im finally zurueckgesetzt.")]
        public async Task ResetLibraryAsync(int scopeIndex)
        {
            IsMaintaining = true;
            MaintenanceStatusText = SafeResourceLoader.Get("ResetRunning", "Bibliothek wird zurückgesetzt …");

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                switch (scopeIndex)
                {
                    case 0:
                        await maintenance.ClearOnlineLibraryAsync();
                        MaintenanceStatusText = SafeResourceLoader.Get("ResetOnlineDone", "Online-Bibliothek wurde zurückgesetzt.");
                        break;
                    case 1:
                        await maintenance.ClearLocalLibraryAsync();
                        MaintenanceStatusText = SafeResourceLoader.Get("ResetLocalDone", "Lokale Bibliothek wurde zurückgesetzt.");
                        break;
                    default:
                        await maintenance.ClearLibraryAsync();
                        MaintenanceStatusText = SafeResourceLoader.Get("ResetAllDone", "Gesamte Bibliothek wurde zurückgesetzt.");
                        break;
                }
            }
            catch (Exception ex)
            {
                MaintenanceStatusText = $"{SafeResourceLoader.Get("SyncFailed", "Fehler")}: {ex.Message}";
            }
            finally
            {
                IsMaintaining = false;
            }
        }

        // ── Persistenz ──────────────────────────────────────────────────────────

        /// <summary>Übernimmt die wartungsbezogenen Werte aus der Entität ohne Change-Callback.</summary>
        /// <param name="settings">Die geladene Entität.</param>
        public void LoadFrom(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _isBatchLoading = true;
            try
            {
                DbPurgeDays = settings.DbPurgeDays;
                ClearCacheOnNextStart = settings.ClearCacheOnNextStart;
                DbBackupEnabled = settings.DbBackupEnabled;
                DbBackupRetentionCount = settings.DbBackupRetentionCount;
            }
            finally
            {
                _isBatchLoading = false;
            }
        }

        /// <summary>Schreibt die wartungsbezogenen Werte in die Entität.</summary>
        /// <param name="settings">Ziel-Entität.</param>
        public void WriteTo(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            // 0 ist erlaubt – bedeutet sofortige Bereinigung aller soft-gelöschten Einträge
            settings.DbPurgeDays = Math.Max(0, DbPurgeDays);
            settings.ClearCacheOnNextStart = ClearCacheOnNextStart;
            settings.DbBackupEnabled = DbBackupEnabled;
            settings.DbBackupRetentionCount = Math.Clamp(DbBackupRetentionCount, 1, 20);
        }

        /// <summary>
        /// Stoppt den Live-View-Timer und gibt ihn frei. Wird von <see cref="SettingsViewModel.Dispose"/>
        /// im Rahmen des Page-Unloads kaskadiert.
        /// </summary>
        public void Dispose()
        {
            if (_liveViewTimer is not null)
            {
                _liveViewTimer.Stop();
                _liveViewTimer.Tick -= OnLiveViewTimerTick;
                _liveViewTimer = null;
            }
        }

        private void MarkAsChanged()
        {
            if (!_isBatchLoading)
            {
                _onUserEdit();
            }
        }
    }
}
