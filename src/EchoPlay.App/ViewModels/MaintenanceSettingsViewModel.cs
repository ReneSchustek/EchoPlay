using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Verwaltung-Tab der Einstellungsseite: Datenbankpflege (Cache
    /// leeren, Purge, VACUUM, Reset) und Sicherung vor Migrationen. Die Entität speichert
    /// <see cref="DbPurgeDays"/>, <see cref="ClearCacheOnNextStart"/>,
    /// <see cref="DbBackupEnabled"/> und <see cref="DbBackupRetentionCount"/>. Die
    /// Protokollansicht liegt in <see cref="LogViewerViewModel"/>.
    /// </summary>
    public sealed class MaintenanceSettingsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Action _onUserEdit;

        private bool _isBatchLoading;
        private int _dbPurgeDays = 30;
        private bool _clearCacheOnNextStart;
        private bool _dbBackupEnabled = true;
        private int _dbBackupRetentionCount = 5;
        private bool _isMaintaining;
        private string _maintenanceStatusText = string.Empty;

        /// <summary>
        /// Initialisiert das Sub-VM mit DI-Scope-Fabrik und Edit-Callback.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankpflege-Services.</param>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public MaintenanceSettingsViewModel(IServiceScopeFactory scopeFactory, Action onUserEdit)
        {
            _scopeFactory = scopeFactory;
            _onUserEdit = onUserEdit;
        }

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
        /// Gültiger Bereich 1–20; ausgeschaltet wird die Sicherung über <see cref="DbBackupEnabled"/>.
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
        /// WinUI 3 hat keinen eingebauten BoolNegation-Converter, dieses Property ersetzt ihn.
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

        /// <summary>
        /// Bereinigt soft-gelöschte Einträge die älter als <see cref="DbPurgeDays"/> Tage sind
        /// und kompaktiert anschließend die SQLite-Datei mit VACUUM.
        /// Läuft bereits eine Wartung, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn Bereinigung und VACUUM durchgelaufen sind.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "DB-Wartung (Purge/VACUUM): SQLite-Locks, Migration-Fehler oder IO-Fehler während Vacuum werden als Nutzer-Status angezeigt; der Command darf nicht reißen, das IsMaintaining-Flag wird im finally zurückgesetzt.")]
        public async Task RunMaintenanceAsync()
        {
            if (_isMaintaining)
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("DbMaintenance");
            IsMaintaining = true;
            MaintenanceStatusText = SafeResourceLoader.Get("MaintenanceStatusCleaning", "Bereinigung läuft …");

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                await maintenance.PurgeAsync(Math.Max(0, DbPurgeDays));
                await maintenance.VacuumAsync();

                MaintenanceStatusText = SafeResourceLoader.Get("MaintenanceStatusCleaned", "Datenbank erfolgreich bereinigt.");
            }
            catch (Exception ex)
            {
                string errorFormat = SafeResourceLoader.Get("MaintenanceStatusError", "Fehler bei der Bereinigung: {0}");
                MaintenanceStatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture, errorFormat, ex.Message);
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
        /// <returns>Der Task ist abgeschlossen, wenn der gewählte Bereich zurückgesetzt ist.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bibliothek-Reset: bulk-delete aller Serien/Episoden/Tracks und Cover-Dateien kann IO-/DB-Fehler werfen; der Command darf nicht reißen, der Status wird angezeigt und IsMaintaining im finally zurückgesetzt.")]
        public async Task ResetLibraryAsync(int scopeIndex)
        {
            // Wie die Bereinigung: Ein zweiter Lauf neben dem ersten griffe auf dieselben Tabellen zu.
            if (_isMaintaining)
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("LibraryReset");
            IsMaintaining = true;
            MaintenanceStatusText = SafeResourceLoader.Get("ResetRunning", "Bibliothek wird zurückgesetzt …");

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                MaintenanceStatusText = await ClearScopeAsync(maintenance, scopeIndex);
            }
            catch (Exception ex)
            {
                string errorFormat = SafeResourceLoader.Get("ResetFailed", "Zurücksetzen fehlgeschlagen: {0}");
                MaintenanceStatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture, errorFormat, ex.Message);
            }
            finally
            {
                IsMaintaining = false;
            }
        }

        /// <summary>Leert den gewählten Bereich und liefert die Erfolgsmeldung dazu.</summary>
        private static async Task<string> ClearScopeAsync(IDatabaseMaintenanceService maintenance, int scopeIndex)
        {
            switch (scopeIndex)
            {
                case 0:
                    await maintenance.ClearOnlineLibraryAsync();
                    return SafeResourceLoader.Get("ResetOnlineDone", "Online-Bibliothek wurde zurückgesetzt.");
                case 1:
                    await maintenance.ClearLocalLibraryAsync();
                    return SafeResourceLoader.Get("ResetLocalDone", "Lokale Bibliothek wurde zurückgesetzt.");
                default:
                    await maintenance.ClearLibraryAsync();
                    return SafeResourceLoader.Get("ResetAllDone", "Gesamte Bibliothek wurde zurückgesetzt.");
            }
        }

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
            // 0 ist erlaubt und bedeutet sofortige Bereinigung aller soft-gelöschten Einträge
            settings.DbPurgeDays = Math.Max(0, DbPurgeDays);
            settings.ClearCacheOnNextStart = ClearCacheOnNextStart;
            settings.DbBackupEnabled = DbBackupEnabled;
            // Dieselbe Obergrenze, die DatabaseInitializer beim Lesen anwendet.
            settings.DbBackupRetentionCount = Math.Clamp(DbBackupRetentionCount, 1, 20);
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
