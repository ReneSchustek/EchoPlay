using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Online-Tab der Einstellungsseite.
    /// Verwaltet den aktiven Metadaten-Provider (Spotify/Apple Music/Keine) und den
    /// Verbindungstest-Zustand. Der eigentliche API-Aufruf wird an
    /// <see cref="IConnectionTestCoordinator"/> delegiert.
    /// </summary>
    public sealed class OnlineSettingsViewModel : ObservableObject
    {
        private readonly IConnectionTestCoordinator _connectionTestCoordinator;
        private readonly Action _onUserEdit;
        private readonly RelayCommand _testConnectionCommand;

        private bool _isBatchLoading;
        private ProviderType _activeProvider = ProviderType.AppleMusic;
        private bool _isTestingConnection;
        private string? _connectionTestResultText;
        private bool? _connectionTestSuccess;

        /// <summary>
        /// Initialisiert das Sub-VM mit dem Test-Coordinator und dem Edit-Callback.
        /// </summary>
        /// <param name="connectionTestCoordinator">Führt den eigentlichen Verbindungstest aus.</param>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public OnlineSettingsViewModel(
            IConnectionTestCoordinator connectionTestCoordinator,
            Action onUserEdit)
        {
            _connectionTestCoordinator = connectionTestCoordinator;
            _onUserEdit                = onUserEdit;
            _testConnectionCommand     = new RelayCommand(() => _ = TestConnectionAsync());
        }

        /// <summary>Aktiver Metadaten-Anbieter.</summary>
        public ProviderType ActiveProvider
        {
            get => _activeProvider;
            set
            {
                if (SetProperty(ref _activeProvider, value))
                {
                    // Testbutton sofort reagieren lassen – bei "Keine" ist kein Test sinnvoll
                    _testConnectionCommand.SetEnabled(!_isTestingConnection && value != ProviderType.None);
                    OnPropertyChanged(nameof(ActiveProviderTag));
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// String-Repräsentation des aktiven Anbieters für die View. Akzeptierte Werte:
        /// <c>"Spotify"</c>, <c>"AppleMusic"</c>, oder <see cref="string.Empty"/>/<see langword="null"/> für „kein Anbieter".
        /// Entkoppelt die View vom Data-Enum <see cref="ProviderType"/>.
        /// </summary>
        public string ActiveProviderTag
        {
            get => _activeProvider switch
            {
                ProviderType.Spotify    => "Spotify",
                ProviderType.AppleMusic => "AppleMusic",
                _                       => string.Empty
            };
            set => ActiveProvider = value switch
            {
                "Spotify"    => ProviderType.Spotify,
                "AppleMusic" => ProviderType.AppleMusic,
                _            => ProviderType.None
            };
        }

        /// <summary>Gibt an, ob gerade ein Verbindungstest läuft.</summary>
        public bool IsTestingConnection
        {
            get => _isTestingConnection;
            private set
            {
                if (SetProperty(ref _isTestingConnection, value))
                {
                    // Button über RelayCommand steuern – bei "Keine" bleibt er dauerhaft deaktiviert
                    _testConnectionCommand.SetEnabled(!value && _activeProvider != ProviderType.None);
                }
            }
        }

        /// <summary>Statustext des letzten Verbindungstests.</summary>
        public string? ConnectionTestResultText
        {
            get => _connectionTestResultText;
            private set => SetProperty(ref _connectionTestResultText, value);
        }

        /// <summary>
        /// Ergebnis des letzten Verbindungstests.
        /// <see langword="null"/> = kein Test, <see langword="true"/> = Erfolg, <see langword="false"/> = Fehler.
        /// </summary>
        public bool? ConnectionTestSuccess
        {
            get => _connectionTestSuccess;
            private set
            {
                if (SetProperty(ref _connectionTestSuccess, value))
                {
                    OnPropertyChanged(nameof(ConnectionTestSuccessVisibility));
                    OnPropertyChanged(nameof(ConnectionTestFailureVisibility));
                    OnPropertyChanged(nameof(ConnectionTestResultVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Erfolgs-Icons.</summary>
        public Visibility ConnectionTestSuccessVisibility =>
            _connectionTestSuccess == true ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Fehler-Icons.</summary>
        public Visibility ConnectionTestFailureVisibility =>
            _connectionTestSuccess == false ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des gesamten Ergebnisbereichs.</summary>
        public Visibility ConnectionTestResultVisibility =>
            _connectionTestSuccess.HasValue ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Befehl zum Starten des Verbindungstests.</summary>
        public ICommand TestConnectionCommand => _testConnectionCommand;

        /// <summary>
        /// Führt den Verbindungstest über den <see cref="IConnectionTestCoordinator"/> aus
        /// und spiegelt das Ergebnis in <see cref="ConnectionTestSuccess"/> und
        /// <see cref="ConnectionTestResultText"/>. Läuft bereits ein Test, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task TestConnectionAsync()
        {
            if (_isTestingConnection || _activeProvider == ProviderType.None)
            {
                return;
            }

            IsTestingConnection      = true;
            ConnectionTestSuccess    = null;
            ConnectionTestResultText = null;

            try
            {
                ConnectionTestResult result = await _connectionTestCoordinator.TestAsync(_activeProvider);

                ConnectionTestSuccess = result.Success;
                ConnectionTestResultText = result.Success
                    ? SafeResourceLoader.Get("ConnectionSuccess", "Verbindung erfolgreich")
                    : $"{SafeResourceLoader.Get("ConnectionFailed", "Verbindung fehlgeschlagen")}: {result.ErrorDetail}";
            }
            finally
            {
                IsTestingConnection = false;
            }
        }

        /// <summary>Übernimmt den Provider aus der Entität ohne Change-Callback.</summary>
        /// <param name="settings">Die geladene Entität.</param>
        public void LoadFrom(AppSettings settings)
        {
            _isBatchLoading = true;
            try
            {
                ActiveProvider = settings.ActiveProvider;
            }
            finally
            {
                _isBatchLoading = false;
            }
        }

        /// <summary>Schreibt den aktuellen Provider in die Entität.</summary>
        /// <param name="settings">Ziel-Entität.</param>
        public void WriteTo(AppSettings settings)
        {
            settings.ActiveProvider = ActiveProvider;
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
