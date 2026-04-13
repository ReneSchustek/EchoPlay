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
        private readonly ISpotifyCredentialStore _credentialStore;
        private readonly ISpotifyOptionsProvider _optionsProvider;
        private readonly Action _onUserEdit;
        private readonly RelayCommand _testConnectionCommand;

        private bool _isBatchLoading;
        private ProviderType _activeProvider = ProviderType.AppleMusic;
        private bool _isTestingConnection;
        private string? _connectionTestResultText;
        private bool? _connectionTestSuccess;
        private string _spotifyClientId = string.Empty;
        private string _spotifyClientSecret = string.Empty;
        private string _spotifyStatus = string.Empty;
        private bool _isSpotifyLinked;
        private bool _isTestingCredentials;

        /// <summary>
        /// Initialisiert das Sub-VM mit dem Test-Coordinator, dem Credential-Store und dem Edit-Callback.
        /// </summary>
        /// <param name="connectionTestCoordinator">Führt den eigentlichen Verbindungstest aus.</param>
        /// <param name="credentialStore">Speichert und liest Spotify-Credentials.</param>
        /// <param name="optionsProvider">Liefert zur Laufzeit die vollständigen SpotifyOptions.</param>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public OnlineSettingsViewModel(
            IConnectionTestCoordinator connectionTestCoordinator,
            ISpotifyCredentialStore credentialStore,
            ISpotifyOptionsProvider optionsProvider,
            Action onUserEdit)
        {
            _connectionTestCoordinator = connectionTestCoordinator;
            _credentialStore           = credentialStore;
            _optionsProvider           = optionsProvider;
            _onUserEdit                = onUserEdit;
            _testConnectionCommand     = new RelayCommand(() => _ = TestConnectionAsync());

            TestAndSaveSpotifyCommand = new RelayCommand(() => _ = TestAndSaveSpotifyAsync());
            RemoveSpotifyCommand      = new RelayCommand(() => _ = RemoveSpotifyAsync());
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
                ProviderType.Both       => "Both",
                _                       => string.Empty
            };
            set => ActiveProvider = value switch
            {
                "Spotify"    => ProviderType.Spotify,
                "AppleMusic" => ProviderType.AppleMusic,
                "Both"       => ProviderType.Both,
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

        /// <summary>Eingegebene Spotify-Client-ID.</summary>
        public string SpotifyClientId
        {
            get => _spotifyClientId;
            set
            {
                if (SetProperty(ref _spotifyClientId, value))
                {
                    _onUserEdit?.Invoke();
                }
            }
        }

        /// <summary>Eingegebenes Spotify-Client-Secret.</summary>
        public string SpotifyClientSecret
        {
            get => _spotifyClientSecret;
            set
            {
                if (SetProperty(ref _spotifyClientSecret, value))
                {
                    _onUserEdit?.Invoke();
                }
            }
        }

        /// <summary>Statustext der Spotify-Verknüpfung.</summary>
        public string SpotifyStatus
        {
            get => _spotifyStatus;
            private set => SetProperty(ref _spotifyStatus, value);
        }

        /// <summary>Ob Spotify-Credentials im Store vorhanden sind.</summary>
        public bool IsSpotifyLinked
        {
            get => _isSpotifyLinked;
            private set => SetProperty(ref _isSpotifyLinked, value);
        }

        /// <summary>Ob gerade ein Credential-Test läuft.</summary>
        public bool IsTestingCredentials
        {
            get => _isTestingCredentials;
            private set => SetProperty(ref _isTestingCredentials, value);
        }

        /// <summary>Befehl zum Testen und Speichern der Spotify-Credentials.</summary>
        public ICommand TestAndSaveSpotifyCommand { get; }

        /// <summary>Befehl zum Entfernen der Spotify-Verknüpfung.</summary>
        public ICommand RemoveSpotifyCommand { get; }

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

        /// <summary>Lädt den Spotify-Verknüpfungsstatus beim Initialisieren.</summary>
        public void LoadSpotifyStatus()
        {
            IsSpotifyLinked = _credentialStore.HasCredentials;

            if (_credentialStore.LastLoadFailedDueToCorruption)
            {
                // Nach Profil-Migration: korrupte Records wurden automatisch gelöscht,
                // der Nutzer muss Credentials neu eingeben.
                SpotifyStatus = "Gespeicherte Credentials konnten nicht entschlüsselt werden. Bitte ClientId und ClientSecret neu eingeben.";
                _credentialStore.AcknowledgeCorruptionNotice();
                return;
            }

            SpotifyStatus = IsSpotifyLinked ? "Verknüpft" : "Nicht verknüpft";
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Spotify-Credential-Test: HTTP-/OAuth-/DPAPI-Fehler beim Token-Abruf und beim verschluesselten Speichern duerfen den Command nicht reissen; der Status wird dem Nutzer in 'SpotifyStatus' angezeigt.")]
        private async Task TestAndSaveSpotifyAsync()
        {
            if (string.IsNullOrWhiteSpace(_spotifyClientId) || string.IsNullOrWhiteSpace(_spotifyClientSecret))
            {
                SpotifyStatus = "ClientId und ClientSecret dürfen nicht leer sein.";
                return;
            }

            IsTestingCredentials = true;
            SpotifyStatus = "Teste Verbindung…";

            try
            {
                await _credentialStore.SaveAsync(_spotifyClientId.Trim(), _spotifyClientSecret.Trim());
                IsSpotifyLinked = true;
                SpotifyStatus = "Verknüpft";
                SpotifyClientId = string.Empty;
                SpotifyClientSecret = string.Empty;
            }
            catch (Exception ex)
            {
                SpotifyStatus = $"Fehler: {ex.Message}";
            }
            finally
            {
                IsTestingCredentials = false;
            }
        }

        private async Task RemoveSpotifyAsync()
        {
            await _credentialStore.ClearAsync();
            IsSpotifyLinked = false;
            SpotifyStatus = "Nicht verknüpft";
            SpotifyClientId = string.Empty;
            SpotifyClientSecret = string.Empty;
        }

        /// <summary>Übernimmt den Provider aus der Entität ohne Change-Callback.</summary>
        /// <param name="settings">Die geladene Entität.</param>
        public void LoadFrom(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
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
            ArgumentNullException.ThrowIfNull(settings);
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
