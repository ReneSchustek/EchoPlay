using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TagLib;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für den vollständigen Player.
    /// Verwaltet eine Playlist aus lokalen MP3-Dateien, steuert die Wiedergabe
    /// über den <see cref="IPlayerService"/> und zeigt Coverbilder aus ID3-Tags.
    /// </summary>
    public sealed class PlayerViewModel : ObservableObject
    {
        private readonly IPlayerService _playerService;
        private readonly IServiceScopeFactory _scopeFactory;

        private ObservableCollection<PlaylistItemViewModel> _playlistItems = [];
        private BitmapImage? _coverImage;
        private bool _isPlaying;
        private string _currentTitle = string.Empty;
        private double _positionSeconds;
        private double _durationSeconds;
        private bool _isSeeking;
        private bool _showRemainingTime = true;
        private string _elapsedText = "0:00";
        private string _remainingOrTotalText = "-0:00";
        private string _playlistTitle = string.Empty;
        private string _playlistSubtitle = string.Empty;

        /// <summary>
        /// Initialisiert das ViewModel und abonniert den <see cref="IPlayerService.StateChanged"/>-Event.
        /// </summary>
        /// <param name="playerService">Zentraler Service für die Audiowiedergabe.</param>
        /// <param name="scopeFactory">Für Datenbankzugriffe (AppSettings, LastOpenedPlayerFolder).</param>
        public PlayerViewModel(IPlayerService playerService, IServiceScopeFactory scopeFactory)
        {
            _playerService = playerService;
            _scopeFactory  = scopeFactory;

            _playerService.StateChanged += OnPlayerStateChanged;

            PlayPauseCommand       = new RelayCommand(() => TogglePlayPause());
            NextCommand            = new RelayCommand(() => _playerService.SkipToNext());
            PreviousCommand        = new RelayCommand(() => _playerService.SkipToPrevious());
            ToggleTimeDisplayCommand = new RelayCommand(() =>
            {
                _showRemainingTime = !_showRemainingTime;
                UpdateTimeDisplay();
            });

            // Initialen Zustand aus dem PlayerService übernehmen – er läuft vielleicht schon
            RefreshFromPlayerService();
        }

        /// <summary>Playlist: alle geladenen Tracks in der Reihenfolge, wie sie abgespielt werden.</summary>
        public ObservableCollection<PlaylistItemViewModel> PlaylistItems
        {
            get => _playlistItems;
            private set => SetProperty(ref _playlistItems, value);
        }

        /// <summary>
        /// Coverbild aus dem ID3-Tag des aktuellen Tracks.
        /// Null wenn kein Cover vorhanden oder kein Track geladen ist.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            private set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    OnPropertyChanged(nameof(NoCoverVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Fallback-Icons: eingeblendet wenn kein Coverbild geladen ist.
        /// </summary>
        public Microsoft.UI.Xaml.Visibility NoCoverVisibility =>
            _coverImage is null
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        /// <summary>Gibt an, ob gerade Wiedergabe aktiv ist.</summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseGlyph));
                }
            }
        }

        /// <summary>
        /// Segoe-Fluent-Icons-Glyph für den Play/Pause-Button.
        /// E769 = Pause, E768 = Play.
        /// </summary>
        public string PlayPauseGlyph => _isPlaying ? "\uE769" : "\uE768";

        /// <summary>Anzeigename des aktuell spielenden Tracks.</summary>
        public string CurrentTitle
        {
            get => _currentTitle;
            private set => SetProperty(ref _currentTitle, value);
        }

        /// <summary>Aktuelle Abspielposition in Sekunden – für den Slider-Wert.</summary>
        public double PositionSeconds
        {
            get => _positionSeconds;
            set
            {
                // Nur während manuellem Seek schreiben – verhindert Rückkopplung vom PlayerService
                if (_isSeeking)
                {
                    SetProperty(ref _positionSeconds, value);
                }
            }
        }

        /// <summary>Gesamtdauer in Sekunden – Maximum des Sliders.</summary>
        public double DurationSeconds
        {
            get => _durationSeconds;
            private set => SetProperty(ref _durationSeconds, value);
        }

        /// <summary>
        /// Formatierte bereits gespielte Zeit, z.B. "3:45" oder "1:03:45".
        /// Wird alle 500 ms aktualisiert.
        /// </summary>
        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        /// <summary>
        /// Formatierte verbleibende Zeit oder Gesamtdauer, je nach <see cref="_showRemainingTime"/>.
        /// Verbleibend: "-23:45", Gesamt: "1:00:00". Per Klick umschaltbar.
        /// </summary>
        public string RemainingOrTotalText
        {
            get => _remainingOrTotalText;
            private set => SetProperty(ref _remainingOrTotalText, value);
        }

        /// <summary>
        /// Wechselt die rechte Zeitanzeige zwischen verbleibender Zeit und Gesamtdauer.
        /// </summary>
        public ICommand ToggleTimeDisplayCommand { get; }

        /// <summary>
        /// Titel der Playlist – Episodentitel oder Ordnername.
        /// Ersetzt die statische Überschrift "Tracks".
        /// </summary>
        public string PlaylistTitle
        {
            get => _playlistTitle;
            private set => SetProperty(ref _playlistTitle, value);
        }

        /// <summary>
        /// Untertitel der Playlist – Gesamtspielzeit und Trackanzahl (z.B. "1:12:34 · 8 Tracks").
        /// </summary>
        public string PlaylistSubtitle
        {
            get => _playlistSubtitle;
            private set => SetProperty(ref _playlistSubtitle, value);
        }

        /// <summary>Play/Pause-Befehl: umschalten zwischen Wiedergabe und Pause.</summary>
        public ICommand PlayPauseCommand { get; }

        /// <summary>Zum nächsten Track springen.</summary>
        public ICommand NextCommand { get; }

        /// <summary>Zum vorherigen Track springen.</summary>
        public ICommand PreviousCommand { get; }

        /// <summary>
        /// Muss aufgerufen werden, wenn der Slider-Drag beginnt.
        /// Setzt das Anti-Feedback-Flag, damit eingehende Position-Updates den Slider nicht zurücksetzen.
        /// </summary>
        public void BeginSeek()
        {
            _isSeeking = true;
        }

        /// <summary>
        /// Muss aufgerufen werden, wenn der Slider-Drag endet.
        /// Übergibt die neue Position an den <see cref="IPlayerService"/> und setzt das Flag zurück.
        /// </summary>
        public void CommitSeek()
        {
            _playerService.SeekTo(TimeSpan.FromSeconds(_positionSeconds));
            _isSeeking = false;
        }

        /// <summary>
        /// Lädt alle Audiodateien aus dem angegebenen Ordner als Playlist.
        /// Unterstützt alle Formate aus <see cref="EchoPlay.Core.AudioExtensions.Supported"/>.
        /// Sortiert nach Dateiname – entspricht in der Regel der Episodenreihenfolge.
        /// </summary>
        /// <param name="folderPath">Ordnerpfad mit den Audiodateien.</param>
        public void LoadFolder(string folderPath)
        {
            // Alle Dateien holen und nach unterstützten Audioformaten filtern –
            // Directory.GetFiles unterstützt kein Multi-Pattern, deshalb filtern wir selbst
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(EchoPlay.Core.AudioExtensions.IsAudioFile)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            BuildPlaylist(files);
        }

        /// <summary>
        /// Lädt die übergebenen Dateipfade als Playlist, ohne einen Ordner zu öffnen.
        /// Verwendung: FileOpenPicker mit Mehrfachauswahl.
        /// </summary>
        /// <param name="filePaths">Absolute Pfade der ausgewählten Audiodateien.</param>
        public void LoadFiles(IReadOnlyList<string> filePaths)
        {
            BuildPlaylist(filePaths);
        }

        /// <summary>
        /// Speichert den Pfad des zuletzt geöffneten Ordners in den AppSettings.
        /// Wird beim nächsten FolderPicker-Aufruf als Startverzeichnis vorgeschlagen.
        /// </summary>
        /// <param name="folderPath">Der zu speichernde Ordnerpfad.</param>
        public async Task SaveLastOpenedFolderAsync(string folderPath)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
            settings.LastOpenedPlayerFolder = folderPath;
            await settingsService.SaveAsync(settings);
        }

        /// <summary>
        /// Gibt den zuletzt geöffneten Ordner aus den AppSettings zurück.
        /// Null wenn noch kein Ordner geöffnet wurde.
        /// </summary>
        /// <returns>Letzter Ordnerpfad oder null.</returns>
        public async Task<string?> GetLastOpenedFolderAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
            return settings.LastOpenedPlayerFolder;
        }

        /// <summary>
        /// Aktualisiert die ViewModel-Properties anhand des aktuellen PlayerService-Zustands.
        /// Wird nach jedem StateChanged-Event aufgerufen.
        /// </summary>
        private void RefreshFromPlayerService()
        {
            IsPlaying    = _playerService.IsPlaying;
            CurrentTitle = _playerService.CurrentTrackTitle ?? string.Empty;
            DurationSeconds = _playerService.Duration.TotalSeconds;

            // Slider nur aktualisieren wenn kein Drag läuft – sonst springt der Slider zurück
            if (!_isSeeking)
            {
                SetProperty(ref _positionSeconds, _playerService.Position.TotalSeconds, nameof(PositionSeconds));
            }

            UpdateTimeDisplay();
            UpdateCurrentTrackHighlight();
        }

        /// <summary>
        /// Hebt den aktuell spielenden Track in der Playlist visuell hervor.
        /// Ermittelt den aktiven Track anhand des Dateinamens aus <see cref="IPlayerService.CurrentTrackTitle"/>.
        /// </summary>
        private void UpdateCurrentTrackHighlight()
        {
            string? currentTitle = _playerService.CurrentTrackTitle;

            foreach (PlaylistItemViewModel item in _playlistItems)
            {
                item.IsCurrentTrack = item.FileName == currentTitle;
            }
        }

        /// <summary>
        /// Aktualisiert die Zeitanzeige-Texte basierend auf der aktuellen Position und Dauer.
        /// </summary>
        private void UpdateTimeDisplay()
        {
            TimeSpan position = _playerService.Position;
            TimeSpan duration = _playerService.Duration;

            ElapsedText = FormatTime(position);

            if (_showRemainingTime)
            {
                TimeSpan remaining = duration - position;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                RemainingOrTotalText = "-" + FormatTime(remaining);
            }
            else
            {
                RemainingOrTotalText = FormatTime(duration);
            }
        }

        /// <summary>
        /// Formatiert eine Zeitspanne als lesbaren Text.
        /// Bei Werten unter einer Stunde: "m:ss", ab einer Stunde: "h:mm:ss".
        /// </summary>
        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }

            return $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
        }

        private void OnPlayerStateChanged(object? sender, EventArgs e)
        {
            RefreshFromPlayerService();
        }

        /// <summary>
        /// Baut die Playlist aus einer Liste von Dateipfaden auf und startet die Wiedergabe.
        /// </summary>
        private void BuildPlaylist(IReadOnlyList<string> paths)
        {
            ObservableCollection<PlaylistItemViewModel> items = new();

            for (int i = 0; i < paths.Count; i++)
            {
                items.Add(new PlaylistItemViewModel(i, paths[i]));
            }

            PlaylistItems = items;

            // Playlist-Header: Ordnername als Titel, Trackanzahl als Untertitel.
            // Gesamtdauer wird erst nach dem Start aktualisiert (Mediaplayer kennt sie vorher nicht).
            if (paths.Count > 0)
            {
                string? folderPath = Path.GetDirectoryName(paths[0]);
                PlaylistTitle = folderPath is not null
                    ? Path.GetFileName(folderPath)
                    : string.Empty;
            }
            else
            {
                PlaylistTitle = string.Empty;
            }

            string trackWord = paths.Count == 1 ? "Track" : "Tracks";
            PlaylistSubtitle = $"{paths.Count} {trackWord}";

            if (paths.Count == 0)
            {
                return;
            }

            // Episodenlosen Standalone-Playback: Guid.Empty unterdrückt PlaybackState-Persistenz im PlayerService
            _playerService.Play(Guid.Empty, paths, startIndex: 0);
            _ = LoadCoverFromId3Async(paths[0]);
        }

        /// <summary>
        /// Spielt den angegebenen Playlist-Eintrag ab.
        /// Wird vom Code-Behind beim Doppelklick auf einen Listeneintrag aufgerufen.
        /// </summary>
        public void PlayItem(PlaylistItemViewModel? item)
        {
            if (item is null || PlaylistItems.Count == 0)
            {
                return;
            }

            List<string> paths = new(PlaylistItems.Count);

            foreach (PlaylistItemViewModel playlistItem in PlaylistItems)
            {
                paths.Add(playlistItem.FullPath);
            }

            _playerService.Play(Guid.Empty, paths, startIndex: item.Index);
            _ = LoadCoverFromId3Async(item.FullPath);
        }

        private void TogglePlayPause()
        {
            if (_playerService.IsPlaying)
            {
                _playerService.Pause();
            }
            else
            {
                _playerService.Resume();
            }
        }

        /// <summary>
        /// Liest das Coverbild asynchron aus dem ID3-Tag der angegebenen Datei.
        /// TagLib# wird auf einem Hintergrundthread ausgeführt, damit der UI-Thread nicht blockiert.
        /// BitmapImage.SetSourceAsync muss auf dem UI-Thread aufgerufen werden.
        /// Wenn kein Cover vorhanden ist, wird <see cref="CoverImage"/> auf null gesetzt.
        /// </summary>
        private async Task LoadCoverFromId3Async(string filePath)
        {
            try
            {
                // Disk-I/O auf Hintergrundthread auslagern – ID3-Tag-Lesen kann bei großen Dateien spürbar sein
                byte[]? imageData = await Task.Run(() =>
                {
                    using TagLib.File tagFile = TagLib.File.Create(filePath);
                    IPicture[] pictures = tagFile.Tag.Pictures;

                    return pictures.Length > 0 ? pictures[0].Data.Data : null;
                });

                if (imageData is null)
                {
                    CoverImage = null;
                    return;
                }

                // Ab hier UI-Thread – BitmapImage und InMemoryRandomAccessStream sind nicht thread-sicher
                using Windows.Storage.Streams.InMemoryRandomAccessStream randomAccessStream = new();
                using Windows.Storage.Streams.DataWriter writer = new(randomAccessStream.GetOutputStreamAt(0));
                writer.WriteBytes(imageData);
                await writer.StoreAsync();

                BitmapImage bitmap = new();
                await bitmap.SetSourceAsync(randomAccessStream);
                CoverImage = bitmap;
            }
            catch (Exception)
            {
                // ID3-Tag fehlt oder ist beschädigt – kein Cover anzeigen, kein Absturz.
                // Häufige Ursachen: korrupte MP3-Datei, fehlende Leserechte, ungültiges Bildformat.
                CoverImage = null;
            }
        }
    }
}
