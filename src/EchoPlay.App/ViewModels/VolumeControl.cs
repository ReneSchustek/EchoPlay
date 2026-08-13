using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Lautstärke-Bedienung: Regler, Stummschaltung und das Merken über den Neustart
    /// hinaus.
    /// </summary>
    /// <remarks>
    /// Beide Wiedergabe-Ansichten zeigen denselben Wert, weil beide denselben
    /// Wiedergabedienst bedienen — hier liegt nur die Bedienung, nicht der Zustand. Deshalb
    /// steht sie einmal und wird von beiden Ansichtsmodellen gehalten, statt zweimal
    /// zu entstehen und dabei auseinanderzulaufen.
    ///
    /// Gespeichert wird erst beim Loslassen (<see cref="CommitAsync"/>): Ein Regler feuert
    /// beim Ziehen dutzende Male, und jede Zwischenstellung in die Datenbank zu schreiben
    /// hieße, den Wiedergabepfad mit Schreibvorgängen zu belegen.
    /// </remarks>
    public sealed class VolumeControl : ObservableObject
    {
        /// <summary>Lautstärke, die beim Aufheben der Stummschaltung mindestens gilt.</summary>
        private const double MinimumAudibleVolume = 0.05;

        private readonly IPlayerService _playerService;
        private readonly IServiceScopeFactory _scopeFactory;

        internal VolumeControl(IPlayerService playerService, IServiceScopeFactory scopeFactory)
        {
            _playerService = playerService;
            _scopeFactory = scopeFactory;

            ToggleMuteCommand = new RelayCommand(() => _ = ToggleMuteAsync());
        }

        /// <summary>
        /// Lautstärke in Prozent (0–100) — der Wert des Reglers.
        /// </summary>
        public double VolumePercent
        {
            get => _playerService.Volume * 100;
            set
            {
                double desired = Math.Clamp(value, 0, 100) / 100;
                if (Math.Abs(_playerService.Volume - desired) < 0.0001)
                {
                    return;
                }

                _playerService.Volume = desired;

                // Wer am Regler dreht, will hören — die Stummschaltung endet damit.
                if (desired > 0 && _playerService.IsMuted)
                {
                    _playerService.IsMuted = false;
                }

                RaiseAll();
            }
        }

        /// <summary>Ob die Wiedergabe stummgeschaltet ist.</summary>
        public bool IsMuted => _playerService.IsMuted;

        /// <summary>Schaltet stumm und wieder zurück.</summary>
        public ICommand ToggleMuteCommand { get; }

        /// <summary>
        /// Das Zeichen des Lautsprechers: durchgestrichen bei Stummschaltung, sonst nach
        /// Lautstärke abgestuft.
        /// </summary>
        public string VolumeGlyph => _playerService.IsMuted || _playerService.Volume <= 0
            ? ""
            : _playerService.Volume < 0.34
                ? ""
                : _playerService.Volume < 0.67
                    ? ""
                    : "";

        /// <summary>
        /// Meldet alle abgeleiteten Werte als geändert. Wird gebraucht, wenn die Lautstärke
        /// von außen gesetzt wurde — etwa beim Start aus den Einstellungen.
        /// </summary>
        public void Refresh() => RaiseAll();

        /// <summary>
        /// Schreibt den aktuellen Stand in die Einstellungen. Von der Oberfläche aufzurufen,
        /// wenn der Nutzer den Regler loslässt.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn gespeichert wurde.</returns>
        public async Task CommitAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            AppSettings settings = await settingsService.GetAsync();
            settings.Volume = _playerService.Volume;
            settings.IsMuted = _playerService.IsMuted;
            await settingsService.SaveAsync(settings);
        }

        /// <summary>
        /// Schaltet stumm oder hebt die Stummschaltung auf und merkt den Stand.
        /// </summary>
        private async Task ToggleMuteAsync()
        {
            bool neuStumm = !_playerService.IsMuted;
            _playerService.IsMuted = neuStumm;

            // Ein voll heruntergedrehter Regler und aufgehobene Stummschaltung ergäben
            // zusammen wieder Stille — dann bekommt die Wiedergabe eine hörbare Grundlage.
            if (!neuStumm && _playerService.Volume <= 0)
            {
                _playerService.Volume = MinimumAudibleVolume;
            }

            RaiseAll();
            await CommitAsync();
        }

        /// <summary>Meldet Reglerwert, Stummschaltung und Zeichen als geändert.</summary>
        private void RaiseAll()
        {
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }
}
