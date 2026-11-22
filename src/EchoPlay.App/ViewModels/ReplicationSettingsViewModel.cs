using System.Collections.ObjectModel;
using System.Globalization;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Core.Replication;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bereich „Abgleich zwischen Geräten“ in den Einstellungen: gemeinsamer Ordner, an oder aus,
    /// „Jetzt abgleichen“, Stand des letzten Durchgangs und die bekannten Geräte.
    /// </summary>
    /// <remarks>
    /// Wirkt sofort wie Export und Einspielen und hängt nicht am Speichern-Knopf der Seite: Wer den
    /// Ordner wählt, will, dass der Abgleich ihn ab jetzt benutzt.
    /// </remarks>
    public sealed class ReplicationSettingsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DeviceReplicationService _service;
        private readonly IDeviceIdentity _device;
        private readonly IConfirmationDialogService _confirm;
        private readonly ILocalizationService _text;
        private readonly ILogger _logger;
        private string? _folderPath;
        private bool _isEnabled;
        private bool _isBusy;
        private string _lastRunText = string.Empty;
        private string _errorText = string.Empty;

        /// <summary>
        /// Initialisiert den Bereich.
        /// </summary>
        /// <param name="scopeFactory">Für Einstellungen und Geräteliste.</param>
        /// <param name="service">Der Abgleich im Hintergrund.</param>
        /// <param name="device">Dieses Gerät.</param>
        /// <param name="confirm">Fragt vor dem Entfernen eines Geräts nach.</param>
        /// <param name="text">Liefert die Anzeigetexte.</param>
        /// <param name="loggerFactory">Die Logger-Fabrik.</param>
        internal ReplicationSettingsViewModel(
            IServiceScopeFactory scopeFactory,
            DeviceReplicationService service,
            IDeviceIdentity device,
            IConfirmationDialogService confirm,
            ILocalizationService text,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _logger = loggerFactory.CreateLogger(nameof(ReplicationSettingsViewModel));
        }

        /// <summary>Die anderen Geräte im Abgleich.</summary>
        public ObservableCollection<ReplicationDeviceItemViewModel> Devices { get; } = [];

        /// <summary>Der gewählte Ordner, oder ein Hinweis, dass noch keiner gewählt ist.</summary>
        public string FolderText => string.IsNullOrWhiteSpace(_folderPath) ? _text.Get("ReplicationNoFolder") : _folderPath;

        /// <summary>Ob der Abgleich an ist.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            private set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>Ob sich der Abgleich einschalten lässt; ohne Ordner nicht.</summary>
        public bool CanEnable => !string.IsNullOrWhiteSpace(_folderPath) && !_isBusy;

        /// <summary>Ob „Jetzt abgleichen“ möglich ist.</summary>
        public bool CanRunNow => _isEnabled && CanEnable;

        /// <summary>Kennung dieses Geräts, damit man es auf den anderen wiedererkennt.</summary>
        public string OwnDeviceText => string.Format(CultureInfo.CurrentCulture, _text.Get("ReplicationOwnDeviceFormat"), ShortName(_device.DeviceId));

        /// <summary>Stand des letzten Durchgangs in dieser Sitzung.</summary>
        public string LastRunText
        {
            get => _lastRunText;
            private set => SetProperty(ref _lastRunText, value);
        }

        /// <summary>Was zuletzt schiefging, oder leer.</summary>
        public string ErrorText
        {
            get => _errorText;
            private set
            {
                if (SetProperty(ref _errorText, value))
                {
                    OnPropertyChanged(nameof(ErrorVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Fehlers.</summary>
        public Visibility ErrorVisibility => string.IsNullOrEmpty(_errorText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Sichtbarkeit der Geräteliste.</summary>
        public Visibility DevicesVisibility => Devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Hinweises, dass noch kein anderes Gerät bekannt ist.</summary>
        public Visibility NoDevicesVisibility => Devices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Lädt Einstellungen, Geräte und den Stand des letzten Durchgangs.</summary>
        /// <returns>Der Task ist abgeschlossen, wenn alles angezeigt wird.</returns>
        public async Task LoadAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>().GetAsync();
            _folderPath = settings.ReplicationFolderPath;
            IsEnabled = settings.ReplicationEnabled;

            IReadOnlyList<ReplicationPeer> peers = await scope.ServiceProvider.GetRequiredService<IReplicationPeerDataService>().GetActiveAsync();
            Devices.Clear();
            foreach (ReplicationPeer peer in peers.Where(p => p.DeviceId != _device.DeviceId))
            {
                Devices.Add(CreateItem(peer));
            }

            ShowLastRun(_service.LastRun);
            RaiseState();
        }

        /// <summary>Setzt den gemeinsamen Ordner; ist der Abgleich an, läuft gleich ein Durchgang.</summary>
        /// <param name="folder">Der gewählte Ordner.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Ordner gespeichert ist.</returns>
        public async Task SetFolderAsync(string folder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);
            await SaveAsync(settings => settings.ReplicationFolderPath = folder);
            _folderPath = folder;
            _logger.Info("Abgleich: gemeinsamer Ordner gesetzt auf {Folder}.", folder);
            WakeIfEnabled();
            RaiseState();
        }

        /// <summary>Schaltet den Abgleich ein oder aus.</summary>
        /// <param name="enabled">Der neue Zustand.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Zustand gespeichert ist.</returns>
        public async Task SetEnabledAsync(bool enabled)
        {
            if (enabled && !CanEnable)
            {
                IsEnabled = false;
                return;
            }

            await SaveAsync(settings => settings.ReplicationEnabled = enabled);
            IsEnabled = enabled;
            _logger.Info("Abgleich {State}.", enabled ? "eingeschaltet" : "ausgeschaltet");
            WakeIfEnabled();
            RaiseState();
        }

        /// <summary>Gleicht sofort ab und zeigt das Ergebnis.</summary>
        /// <returns>Der Task ist abgeschlossen, wenn der Durchgang fertig ist.</returns>
        public async Task RunNowAsync()
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("ReplicationRunNow");
            SetBusy(true);
            ReplicationRun? run;
            try
            {
                run = await _service.RunOnceAsync();
            }
            finally
            {
                SetBusy(false);
            }

            await LoadAsync();

            // Kein Durchgang trotz eingeschaltetem Abgleich: Der Ordner ist nicht erreichbar.
            if (run is null)
            {
                ErrorText = _text.Get("ReplicationFolderUnreachable");
            }
        }

        /// <summary>Entfernt ein Gerät nach Rückfrage; sein Protokoll wird danach nicht mehr gelesen.</summary>
        /// <param name="deviceId">Das Gerät.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Liste neu geladen ist.</returns>
        public async Task RemoveDeviceAsync(Guid deviceId)
        {
            string message = string.Format(CultureInfo.CurrentCulture, _text.Get("ReplicationRemoveMessage"), ShortName(deviceId));
            if (!await _confirm.ConfirmAsync(_text.Get("ReplicationRemoveTitle"), message, DialogKey.ReplicationDeviceRemove))
            {
                return;
            }

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                _ = await scope.ServiceProvider.GetRequiredService<IReplicationPeerDataService>().RemoveAsync(deviceId);
            }

            _logger.Info("Abgleich: Gerät {Device} entfernt.", deviceId.ToString("N"));
            await LoadAsync();
        }

        /// <summary>Kurzname eines Geräts: die ersten acht Stellen der Kennung, wie sie im Ordner stehen.</summary>
        internal static string ShortName(Guid deviceId) => deviceId.ToString("N")[..8];

        private ReplicationDeviceItemViewModel CreateItem(ReplicationPeer peer)
        {
            string lastSeen = peer.LastSeenUtc is { } seen
                ? string.Format(CultureInfo.CurrentCulture, _text.Get("ReplicationLastSeenFormat"), seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : _text.Get("ReplicationNeverSeen");
            string applied = string.Format(CultureInfo.CurrentCulture, _text.Get("ReplicationAppliedFormat"), peer.AppliedRecords);
            Guid id = peer.DeviceId;
            return new ReplicationDeviceItemViewModel(
                id, ShortName(id), lastSeen, applied, peer.LastError,
                new RelayCommand(() => DetachedTask.Observe(RemoveDeviceAsync(id), _logger)));
        }

        private void ShowLastRun(ReplicationRun? run)
        {
            if (run is null)
            {
                LastRunText = _text.Get("ReplicationNoRunYet");
                ErrorText = string.Empty;
                return;
            }

            LastRunText = string.Format(
                CultureInfo.CurrentCulture, _text.Get("ReplicationLastRunFormat"),
                run.FinishedUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), run.Received.Changed, run.Published);
            ErrorText = run.Error ?? string.Join(Environment.NewLine, run.Received.Problems);
        }

        private async Task SaveAsync(Action<AppSettings> change)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settings = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings current = await settings.GetAsync();
            change(current);
            await settings.SaveAsync(current);
        }

        private void WakeIfEnabled()
        {
            if (_isEnabled && !string.IsNullOrWhiteSpace(_folderPath))
            {
                _service.RequestRun();
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            RaiseState();
        }

        private void RaiseState()
        {
            OnPropertyChanged(nameof(FolderText));
            OnPropertyChanged(nameof(CanEnable));
            OnPropertyChanged(nameof(CanRunNow));
            OnPropertyChanged(nameof(DevicesVisibility));
            OnPropertyChanged(nameof(NoDevicesVisibility));
        }
    }
}
