using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

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
        private readonly DispatcherQueue? _dispatcherQueue;

        private ObservableCollection<PlaylistItemViewModel> _playlistItems = [];
        private Task _pendingTitleFill = Task.CompletedTask;
        private BitmapImage? _coverImage;
        private bool _isPlaying;
        private string _currentTitle = string.Empty;
        private double _positionSeconds;
        private double _durationSeconds;
        private double _episodeProgressPercent;
        private string _episodeProgressText = string.Empty;

        // Einmal zerlegt statt bei jedem Positions-Tick.
        private static readonly System.Text.CompositeFormat EpisodeProgressFormat =
            System.Text.CompositeFormat.Parse(
                EchoPlay.App.Helpers.SafeResourceLoader.Get("PlayerEpisodeProgressFormat", "Ganze Folge: {0} von {1}"));
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
        /// <param name="scopeFactory">Für Datenbankzugriffe (AppSettings, LastOpenedPlayerFolder) und den Cover-Loader.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "DispatcherQueue.GetForCurrentThread() wirft in WinRT-losen Prozessen (Unit-Test-Host) native/COM-Fehler; der Fallback auf 'null' erlaubt das VM auch außerhalb von WinUI zu konstruieren.")]
        public PlayerViewModel(IPlayerService playerService, IServiceScopeFactory scopeFactory)
        {
            _playerService = playerService;
            _scopeFactory = scopeFactory;
            Volume = new VolumeControl(playerService, scopeFactory);

            // GetForCurrentThread() wirft in WinRT-losen Prozessen (z.B. Unit-Tests) – daher try-catch.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (Exception)
            {
                _dispatcherQueue = null;
            }

            _playerService.StateChanged += OnPlayerStateChanged;

            PlayPauseCommand = new RelayCommand(() => TogglePlayPause());
            NextCommand = new RelayCommand(() => _playerService.SkipToNext());
            PreviousCommand = new RelayCommand(() => _playerService.SkipToPrevious());
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

        /// <summary>
        /// Lautstärke und Stummschaltung. Beide Wiedergabe-Ansichten zeigen denselben Wert,
        /// weil beide denselben Wiedergabedienst bedienen.
        /// </summary>
        public VolumeControl Volume { get; }

        /// <summary>Aktuelle Abspielposition in Sekunden – für den Slider-Wert.</summary>
        public double PositionSeconds
        {
            get => _positionSeconds;
            set
            {
                // Nur während manuellem Seek schreiben – verhindert Rückkopplung vom PlayerService
                if (_isSeeking)
                {
                    _ = SetProperty(ref _positionSeconds, value);
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
        /// Fortschritt in der ganzen Folge, in Prozent (0–100).
        /// </summary>
        /// <remarks>
        /// Der Regler darüber zeigt die laufende Datei. Bei einer Folge aus vier Dateien
        /// sagt „Minute 12" allein aber nichts — erst dieser Wert beantwortet, wie weit man
        /// im Hörspiel ist.
        /// </remarks>
        public double EpisodeProgressPercent
        {
            get => _episodeProgressPercent;
            private set => SetProperty(ref _episodeProgressPercent, value);
        }

        /// <summary>
        /// Fortschritt der Folge als Text, etwa „1:12:30 von 4:41:44".
        /// </summary>
        public string EpisodeProgressText
        {
            get => _episodeProgressText;
            private set => SetProperty(ref _episodeProgressText, value);
        }

        /// <summary>
        /// Sichtbarkeit des Folgen-Fortschritts. Er erscheint nur, wenn die Gesamtdauer
        /// bekannt ist und die Folge aus mehr als einer Datei besteht — sonst doppelte er
        /// nur den Regler darüber.
        /// </summary>
        public Microsoft.UI.Xaml.Visibility EpisodeProgressVisibility =>
            _playerService.OverallDuration > TimeSpan.Zero && _playerService.CurrentTrackPaths.Count > 1
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

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
            ArgumentNullException.ThrowIfNull(filePaths);
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
            AdoptRunningPlaylist();

            IsPlaying = _playerService.IsPlaying;
            DurationSeconds = _playerService.Duration.TotalSeconds;

            // Slider nur aktualisieren wenn kein Drag läuft – sonst springt der Slider zurück
            if (!_isSeeking)
            {
                _ = SetProperty(ref _positionSeconds, _playerService.Position.TotalSeconds, nameof(PositionSeconds));
            }

            UpdateTimeDisplay();
            UpdateCurrentTrackHighlight();
            UpdateCurrentTitle();
        }

        /// <summary>
        /// Setzt die Überschrift auf den Anzeigenamen der laufenden Zeile, damit oben derselbe
        /// Titel steht wie unten in der Liste. Steht die Zeile (noch) nicht in der Liste,
        /// bleibt der Dateiname aus dem Wiedergabe-Dienst.
        /// </summary>
        private void UpdateCurrentTitle()
        {
            PlaylistItemViewModel? current = _playlistItems.FirstOrDefault(item => item.IsCurrentTrack);

            CurrentTitle = current?.Title ?? _playerService.CurrentTrackTitle ?? string.Empty;
        }

        /// <summary>
        /// Trägt die Titel aus den Kennzeichnungen in die eben aufgebaute Wiedergabeliste nach
        /// und zieht die Überschrift mit.
        /// </summary>
        /// <param name="rows">Die Zeilen der Wiedergabeliste.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Titel stehen.</returns>
        internal async Task FillTitlesAsync(IReadOnlyList<ITrackTitleTarget> rows)
        {
            await TrackTitleFiller.FillAsync(_scopeFactory, rows);
            UpdateCurrentTitle();
        }

        /// <summary>Der Vorgang, der die Titel der Wiedergabeliste nachträgt — für Tests.</summary>
        internal Task PendingTitleFill => _pendingTitleFill;

        /// <summary>
        /// Übernimmt die Wiedergabeliste, die gerade läuft, wenn sie eine andere ist als die
        /// angezeigte. Nötig, weil die Wiedergabe meist woanders startet — aus der Mediathek,
        /// der Serienansicht oder über „Weiterhören". Ohne diesen Abgleich zeigt die Seite den
        /// Platzhalter, während unten die Folge läuft.
        /// </summary>
        private void AdoptRunningPlaylist()
        {
            IReadOnlyList<string> running = _playerService.CurrentTrackPaths;

            if (running.Count == 0 || SameAsDisplayed(running))
            {
                return;
            }

            BuildPlaylist(running);
            PlaylistSubtitle = $"{running.Count} {(running.Count == 1 ? "Track" : "Tracks")}";

            // Das Cover der laufenden Spur, nicht das der ersten: Wer mitten in einer Folge
            // einsteigt, soll sehen, was gerade läuft.
            string? currentPath = _playerService.CurrentTrackPath;
            string cover = running.FirstOrDefault(
                p => string.Equals(p, currentPath, StringComparison.OrdinalIgnoreCase))
                ?? running[0];

            _ = LoadCoverFromId3Async(cover);
        }

        /// <summary>
        /// Prüft, ob die angezeigte Liste bereits dieselbe ist wie die übergebene. Ohne diesen
        /// Vergleich würde die Liste bei jedem Positions-Tick neu aufgebaut.
        /// </summary>
        private bool SameAsDisplayed(IReadOnlyList<string> paths)
        {
            if (_playlistItems.Count != paths.Count)
            {
                return false;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (!string.Equals(_playlistItems[i].FullPath, paths[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Hebt den aktuell spielenden Track in der Playlist visuell hervor.
        /// Verglichen wird der Dateipfad aus <see cref="IPlayerService.CurrentTrackPath"/> —
        /// der Anzeigename taugt dafür nicht, weil er aus der Kennzeichnung der Datei
        /// stammen kann und dann nicht mehr zum Dateinamen passt.
        /// </summary>
        private void UpdateCurrentTrackHighlight()
        {
            string? currentPath = _playerService.CurrentTrackPath;

            foreach (PlaylistItemViewModel item in _playlistItems)
            {
                item.IsCurrentTrack = currentPath is not null
                    && string.Equals(item.FullPath, currentPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Zieht den Fortschritt über die ganze Folge nach.
        /// </summary>
        private void UpdateEpisodeProgress()
        {
            TimeSpan overallPosition = _playerService.OverallPosition;
            TimeSpan overallDuration = _playerService.OverallDuration;

            EpisodeProgressPercent = overallDuration > TimeSpan.Zero
                ? Math.Min(100, overallPosition.TotalSeconds / overallDuration.TotalSeconds * 100)
                : 0;

            EpisodeProgressText = overallDuration > TimeSpan.Zero
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    EpisodeProgressFormat,
                    FormatTime(overallPosition),
                    FormatTime(overallDuration))
                : string.Empty;

            OnPropertyChanged(nameof(EpisodeProgressVisibility));
        }

        /// <summary>
        /// Aktualisiert die Zeitanzeige-Texte basierend auf der aktuellen Position und Dauer.
        /// </summary>
        private void UpdateTimeDisplay()
        {
            TimeSpan position = _playerService.Position;
            TimeSpan duration = _playerService.Duration;

            ElapsedText = FormatTime(position);
            UpdateEpisodeProgress();

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
            // StateChanged feuert aus dem Positions-Timer-Thread des PlayerService – die
            // gebundenen Properties dürfen aber nur auf dem UI-Thread verändert werden.
            // In Tests gibt es keinen UI-Thread, daher direkt aktualisieren.
            if (_dispatcherQueue is not null)
            {
                _ = _dispatcherQueue.TryEnqueue(RefreshFromPlayerService);
            }
            else
            {
                RefreshFromPlayerService();
            }
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
            _pendingTitleFill = FillTitlesAsync(items);

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

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("PlayerPlayItem");
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
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("PlayerTogglePlayPause");
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Extraktion aus ID3-Tag: TagLib-Fehler (CorruptFileException, UnsupportedFormat) oder IO-Fehler dürfen die Wiedergabe nicht stören – der Platzhalter bleibt stehen.")]
        private async Task LoadCoverFromId3Async(string filePath)
        {
            try
            {
                // Cover-Extraktion aus dem ID3-Tag ist im LocalCoverLoader gekapselt (inkl.
                // Hintergrundthread und Fehlerbehandlung). Kein Episodenordner → nur der ID3-Pfad.
                // Scoped-Service über die ScopeFactory auflösen (analog BackgroundCoverService).
                using IServiceScope coverScope = _scopeFactory.CreateScope();
                ILocalCoverLoader coverLoader = coverScope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();
                byte[]? imageData = await coverLoader.LoadAsync(episodeFolderPath: null, firstTrackPath: filePath);

                if (imageData is null)
                {
                    CoverImage = null;
                    return;
                }

                // Ab hier UI-Thread – BitmapImage und InMemoryRandomAccessStream sind nicht thread-sicher
                using Windows.Storage.Streams.InMemoryRandomAccessStream randomAccessStream = new();
                using Windows.Storage.Streams.DataWriter writer = new(randomAccessStream.GetOutputStreamAt(0));
                writer.WriteBytes(imageData);
                _ = await writer.StoreAsync();

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
