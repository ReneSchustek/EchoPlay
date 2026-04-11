using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Models;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Allgemein-Tab der Einstellungsseite.
    /// Kapselt Theme-, Sprach-, Neuerscheinungs- und Log-Schwellwert-Zustand.
    /// Änderungen werden über einen Callback an das übergeordnete <see cref="SettingsViewModel"/>
    /// gemeldet, das <c>HasUnsavedChanges</c> aggregiert und die StatusBar informiert.
    /// </summary>
    public sealed class GeneralSettingsViewModel : ObservableObject
    {
        private readonly Action _onUserEdit;

        private bool _isBatchLoading;
        private string _activeTheme = "MidnightLibrary";
        private string _activeLanguage = "de";
        private int _newReleaseDays = 60;
        private bool _offlineMode;
        private bool _onlineOnlyMode;
        private int _logRetentionDays = 30;
        private LogLevel _minimumLogLevel = LogLevel.Information;

        /// <summary>
        /// Initialisiert das Sub-VM mit dem Callback für Nutzeränderungen.
        /// Der Callback wird bei jeder Property-Änderung außerhalb von <see cref="LoadFrom"/> aufgerufen.
        /// </summary>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public GeneralSettingsViewModel(Action onUserEdit)
        {
            _onUserEdit = onUserEdit;
        }

        /// <summary>Name des aktiven Farbthemas.</summary>
        public string ActiveTheme
        {
            get => _activeTheme;
            set
            {
                if (SetProperty(ref _activeTheme, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>BCP-47-Sprachcode der aktiven Benutzeroberflächen-Sprache.</summary>
        public string ActiveLanguage
        {
            get => _activeLanguage;
            set
            {
                if (SetProperty(ref _activeLanguage, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Liste der verfügbaren Sprachen für die UI-Auswahl.
        /// Einmalig beim Konstruktor initialisiert – keine Datenbankabfrage nötig.
        /// </summary>
        public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        [
            new("de", "Deutsch"),
            new("en", "English")
        ];

        /// <summary>
        /// Zeitfenster in Tagen für den Neuerscheinungen-Filter auf dem Dashboard.
        /// Gültiger Bereich 7–365; wird beim Schreiben in die Entität geklammert.
        /// </summary>
        public int NewReleaseDays
        {
            get => _newReleaseDays;
            set
            {
                if (SetProperty(ref _newReleaseDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Offline-Modus: deaktiviert alle Online-Abfragen (iTunes, Serien-Suche).
        /// </summary>
        public bool OfflineMode
        {
            get => _offlineMode;
            set
            {
                if (SetProperty(ref _offlineMode, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Nur-Online-Modus: blendet die lokale Mediathek aus.
        /// </summary>
        public bool OnlineOnlyMode
        {
            get => _onlineOnlyMode;
            set
            {
                if (SetProperty(ref _onlineOnlyMode, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Aufbewahrungszeit für Log-Dateien in Tagen.
        /// Mindestwert 1 wird beim Schreiben in die Entität sichergestellt.
        /// </summary>
        public int LogRetentionDays
        {
            get => _logRetentionDays;
            set
            {
                if (SetProperty(ref _logRetentionDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Mindest-Log-Level das überhaupt geschrieben werden soll.
        /// Einträge unterhalb landen weder in der Datei noch im Live-Puffer.
        /// </summary>
        public LogLevel MinimumLogLevel
        {
            get => _minimumLogLevel;
            set
            {
                if (SetProperty(ref _minimumLogLevel, value))
                {
                    OnPropertyChanged(nameof(MinimumLogLevelIndex));
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Index für die MinimumLogLevel-ComboBox (0=Trace, 1=Debug, 2=Information, 3=Warning, 4=Error).
        /// Mappt bidirektional auf <see cref="MinimumLogLevel"/>.
        /// </summary>
        public int MinimumLogLevelIndex
        {
            get => _minimumLogLevel switch
            {
                LogLevel.Trace       => 0,
                LogLevel.Debug       => 1,
                LogLevel.Information => 2,
                LogLevel.Warning     => 3,
                _                    => 4
            };
            set => MinimumLogLevel = value switch
            {
                0 => LogLevel.Trace,
                1 => LogLevel.Debug,
                3 => LogLevel.Warning,
                4 => LogLevel.Error,
                _ => LogLevel.Information
            };
        }

        /// <summary>
        /// Übernimmt alle Werte aus der Entität ohne Nutzerbenachrichtigung.
        /// Das Batch-Load-Flag verhindert, dass der <c>onUserEdit</c>-Callback feuert.
        /// </summary>
        /// <param name="settings">Die geladene Entität.</param>
        public void LoadFrom(AppSettings settings)
        {
            _isBatchLoading = true;
            try
            {
                ActiveTheme      = settings.ActiveTheme;
                ActiveLanguage   = settings.ActiveLanguage;
                NewReleaseDays   = settings.NewReleaseDays;
                OfflineMode      = settings.OfflineMode;
                OnlineOnlyMode   = settings.OnlineOnlyMode;
                LogRetentionDays = settings.LogRetentionDays;
                MinimumLogLevel  = settings.MinimumLogLevel;
            }
            finally
            {
                _isBatchLoading = false;
            }
        }

        /// <summary>
        /// Schreibt alle gehaltenen Werte in die Entität. Werte werden bei Bedarf geklammert
        /// (z.B. <see cref="NewReleaseDays"/> auf 7–365, <see cref="LogRetentionDays"/> ≥ 1).
        /// </summary>
        /// <param name="settings">Die Entität, in die geschrieben wird.</param>
        public void WriteTo(AppSettings settings)
        {
            settings.ActiveTheme      = ActiveTheme;
            settings.ActiveLanguage   = ActiveLanguage;
            // Mindestwert 7, Maximum 365 – unter einer Woche sind Neuerscheinungen kaum sinnvoll
            settings.NewReleaseDays   = Math.Clamp(NewReleaseDays, 7, 365);
            settings.OfflineMode      = OfflineMode;
            settings.OnlineOnlyMode   = OnlineOnlyMode;
            // Mindestwert 1 sicherstellen – 0 Tage würde alle Logs sofort löschen
            settings.LogRetentionDays = Math.Max(1, LogRetentionDays);
            settings.MinimumLogLevel  = MinimumLogLevel;
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
