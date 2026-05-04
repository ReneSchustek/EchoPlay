using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Serien-Detailansicht.
    /// Zeigt Episoden als sortierbare Kacheln und lädt beim Anwählen einer Episode
    /// die zugehörigen lokalen Tracks in die zweite Spalte.
    /// Online-Serien ohne lokale Dateien zeigen eine entsprechende Leer-Meldung.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Der einzige verwerfbare Zustand (_priorityCts) wird über CancelPendingPriorityLoad deterministisch freigegeben, das vom OnNavigatedFrom-Pfad der Page aufgerufen wird; ein eigener Dispose-Kontrakt im Transient-VM wuerde der bestehenden VM-Konvention widersprechen.")]
    public sealed class SeriesDetailViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPlayerService _playerService;
        private readonly CoverService? _coverService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly IClock _clock;
        private CancellationTokenSource? _priorityCts;

        private string _seriesTitle = string.Empty;
        private string _seriesDescription = string.Empty;
        private Guid _seriesId;
        private bool _isFavorite;
        private List<EpisodeTileViewModel> _allEpisodes = [];
        private IReadOnlyList<EpisodeTileViewModel> _episodes = [];
        private IReadOnlyList<LocalTrackRowViewModel> _tracks = [];
        private EpisodeTileViewModel? _selectedEpisode;
        private EpisodeSortOrder _sortOrder = EpisodeSortOrder.EpisodeNumber;
        private int _episodeFilterIndex;
        private int _episodeTabIndex;
        private bool _isLoading;
        private bool _hasLocalTracks;
        private string _progressText = string.Empty;
        private double _overallProgressPercent;

        /// <summary>
        /// Initialisiert das ViewModel mit der Scope-Fabrik für Datenbankzugriffe.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für Scoped-Services.</param>
        /// <param name="playerService">Der zentrale Wiedergabe-Service.</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. Nullable für Tests.</param>
        /// <param name="backgroundCoverService">
        /// Optionaler Hintergrund-Cover-Service. Wenn gesetzt, priorisiert das VM
        /// die Folgen-Cover der geöffneten Serie und pausiert damit den laufenden
        /// Hintergrund-Scan, bis die sichtbare Serie versorgt ist.
        /// </param>
        public SeriesDetailViewModel(
            IServiceScopeFactory scopeFactory,
            IPlayerService playerService,
            IClock clock,
            CoverService? coverService = null,
            BackgroundCoverService? backgroundCoverService = null)
        {
            _scopeFactory = scopeFactory;
            _playerService = playerService;
            _clock = clock;
            _coverService = coverService;
            _backgroundCoverService = backgroundCoverService;

            ToggleFavoriteCommand = new RelayCommand(() => _ = ToggleFavoriteAsync());
        }

        /// <summary>Titel der aktuell angezeigten Serie.</summary>
        public string SeriesTitle
        {
            get => _seriesTitle;
            private set => SetProperty(ref _seriesTitle, value);
        }

        /// <summary>Beschreibung der Serie (vom Provider oder manuell).</summary>
        public string SeriesDescription
        {
            get => _seriesDescription;
            private set => SetProperty(ref _seriesDescription, value);
        }

        /// <summary>Sichtbarkeit der Beschreibung – nur wenn Text vorhanden.</summary>
        public Microsoft.UI.Xaml.Visibility DescriptionVisibility =>
            string.IsNullOrWhiteSpace(_seriesDescription)
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;

        /// <summary>
        /// Gibt an, ob die aktuell angezeigte Serie als Favorit markiert ist.
        /// Wird auf der Seite als Stern-Symbol dargestellt.
        /// </summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            private set
            {
                if (SetProperty(ref _isFavorite, value))
                {
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        /// <summary>
        /// Setzt oder entfernt den Favoritenstatus der aktuellen Serie.
        /// </summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>
        /// Glyph für das Favoriten-Symbol: gefüllter Stern wenn favorisiert, leerer Stern sonst.
        /// Verwendet Segoe Fluent Icons (&#xE735; = gefüllt, &#xE734; = leer).
        /// </summary>
        public string FavoriteGlyph => _isFavorite ? "\uE735" : "\uE734";

        /// <summary>
        /// Episodenliste, sortiert nach <see cref="SortOrder"/>.
        /// Ändert sich automatisch wenn <see cref="SortOrder"/> gesetzt wird.
        /// </summary>
        public IReadOnlyList<EpisodeTileViewModel> Episodes
        {
            get => _episodes;
            private set
            {
                if (SetProperty(ref _episodes, value))
                {
                    OnPropertyChanged(nameof(EpisodesEmptyVisibility));
                }
            }
        }

        /// <summary>
        /// Lokale Tracks der aktuell gewählten Episode.
        /// Leer wenn keine Episode gewählt oder keine lokalen Dateien vorhanden sind.
        /// </summary>
        public IReadOnlyList<LocalTrackRowViewModel> Tracks
        {
            get => _tracks;
            private set
            {
                if (SetProperty(ref _tracks, value))
                {
                    OnPropertyChanged(nameof(TracksEmptyVisibility));
                    OnPropertyChanged(nameof(TrackActionsVisibility));
                    OnPropertyChanged(nameof(NoLocalTracksVisibility));
                }
            }
        }

        /// <summary>
        /// Die aktuell gewählte Episode.
        /// <see langword="null"/> solange noch keine Episode angeklickt wurde.
        /// </summary>
        public EpisodeTileViewModel? SelectedEpisode
        {
            get => _selectedEpisode;
            private set => SetProperty(ref _selectedEpisode, value);
        }

        /// <summary>
        /// Sortierkriterium für die Episodenliste.
        /// Jede Änderung löst eine Neusortierung von <see cref="_allEpisodes"/> aus.
        /// </summary>
        public EpisodeSortOrder SortOrder
        {
            get => _sortOrder;
            set
            {
                if (SetProperty(ref _sortOrder, value))
                {
                    ApplySortOrder();
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gesamtfortschritts-Text, z.B. "42 von 229 Folgen gehört".
        /// </summary>
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        /// <summary>
        /// Gesamtfortschritt in Prozent (0–100) für den Fortschrittsbalken im Header.
        /// </summary>
        public double OverallProgressPercent
        {
            get => _overallProgressPercent;
            private set => SetProperty(ref _overallProgressPercent, value);
        }

        /// <summary>
        /// Aktuell gewählter Filter (0 = Alle, 1 = Ungehört, 2 = Gehört, 3 = Angefangen).
        /// Eine Änderung filtert und sortiert die Episode-Liste sofort neu.
        /// </summary>
        public int EpisodeFilterIndex
        {
            get => _episodeFilterIndex;
            set
            {
                if (SetProperty(ref _episodeFilterIndex, value))
                {
                    ApplySortOrder();
                }
            }
        }

        /// <summary>
        /// Aktiver Tab: 0 = Folgen, 1 = Sonderfolgen.
        /// </summary>
        public int EpisodeTabIndex
        {
            get => _episodeTabIndex;
            set
            {
                if (SetProperty(ref _episodeTabIndex, value))
                {
                    ApplySortOrder();
                }
            }
        }

        /// <summary>True wenn Sonderfolgen vorhanden sind.</summary>
        public bool HasSpecialEpisodes => _allEpisodes.Any(e => e.IsSpecialEpisode);

        /// <summary>Anzahl der Sonderfolgen.</summary>
        public int SpecialEpisodeCount => _allEpisodes.Count(e => e.IsSpecialEpisode);

        // ── Berechnete Sichtbarkeiten ────────────────────────────────────────────

        /// <summary>
        /// Sichtbarkeit des Leer-Hinweises für die Episodenliste.
        /// Eingeblendet wenn keine Episoden vorhanden sind.
        /// </summary>
        public Visibility EpisodesEmptyVisibility =>
            _episodes.Count == 0 && !_isLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Folge wählen"-Hinweises in der Track-Spalte.
        /// Eingeblendet solange keine Episode gewählt wurde.
        /// </summary>
        public Visibility TracksEmptyVisibility =>
            _selectedEpisode is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Keine lokalen Dateien"-Hinweises.
        /// Eingeblendet wenn eine Episode gewählt ist, aber keine lokalen Tracks vorhanden sind.
        /// </summary>
        public Visibility NoLocalTracksVisibility =>
            _selectedEpisode is not null && !_hasLocalTracks ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Aktionsleiste ("Ganze Folge abspielen"-Button).
        /// Nur sichtbar wenn lokale Tracks geladen sind.
        /// </summary>
        public Visibility TrackActionsVisibility =>
            _tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt alle Episoden der angegebenen Serie samt Wiedergabestatus.
        /// Startet zusätzlich – sofern ein <see cref="BackgroundCoverService"/> injiziert ist –
        /// die Priorisierung der fehlenden Folgen-Cover dieser Serie und pausiert damit
        /// den Hintergrund-Scan, bis die sichtbare Liste versorgt ist.
        /// </summary>
        /// <param name="seriesId">ID der anzuzeigenden Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync(Guid seriesId)
        {
            // Priorität einer vorherigen Detailansicht sauber beenden, bevor wir neu starten.
            CancelPendingPriorityLoad();

            IsLoading = true;
            Tracks = [];
            SelectedEpisode = null;
            _hasLocalTracks = false;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                Series? series = await seriesService.GetByIdAsync(seriesId);
                _seriesId = seriesId;
                SeriesTitle = series?.Title ?? string.Empty;
                SeriesDescription = series?.Description ?? string.Empty;
                IsFavorite = series?.IsFavorite ?? false;
                OnPropertyChanged(nameof(DescriptionVisibility));

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);

                // Alle PlaybackStates in einem Query laden – verhindert N+1
                IReadOnlyList<PlaybackState> allStates = await playbackService.GetAllAsync();

                Dictionary<Guid, PlaybackState> stateById = new(allStates.Count);
                foreach (PlaybackState state in allStates)
                {
                    stateById[state.EpisodeId] = state;
                }

                List<EpisodeTileViewModel> tiles = new(episodes.Count);

                int completedCount = 0;

                foreach (Episode episode in episodes)
                {
                    _ = stateById.TryGetValue(episode.Id, out PlaybackState? episodeState);
                    PlaybackStatus playbackStatus = DetermineStatus(episodeState);

                    if (playbackStatus == PlaybackStatus.Finished)
                    {
                        completedCount++;
                    }

                    // Fortschritt pro Episode: Position / Dauer * 100
                    double progressPercent = 0;
                    if (episodeState is not null && episode.Duration > TimeSpan.Zero)
                    {
                        progressPercent = episodeState.IsCompleted
                            ? 100
                            : Math.Min(100, episodeState.LastPosition.TotalSeconds / episode.Duration.TotalSeconds * 100);
                    }

                    Guid capturedId = episode.Id;

                    // Episoden-Cover laden: cover.jpg im Folgenordner, dann Serien-Cover als Fallback
                    BitmapImage? cover = await BuildEpisodeCoverAsync(episode)
                                         ?? await BuildSeriesCoverAsync(series);

                    EpisodeTileViewModel tile = new(
                        episodeId: episode.Id,
                        episodeNumber: episode.EpisodeNumber,
                        title: episode.Title,
                        totalDuration: episode.Duration > TimeSpan.Zero ? episode.Duration : null,
                        playbackStatus: playbackStatus,
                        releaseDate: episode.ReleaseDate,
                        playEpisode: () => _ = PlayEpisodeAsync(capturedId),
                        progressPercent: progressPercent,
                        isSpecialEpisode: episode.EpisodeNumber is null or 0,
                        coverImage: cover);

                    tiles.Add(tile);
                }

                _allEpisodes = tiles;

                // Gesamtfortschritt der Serie
                ProgressText = episodes.Count > 0
                    ? $"{completedCount} von {episodes.Count} Folgen gehört"
                    : string.Empty;
                OverallProgressPercent = episodes.Count > 0
                    ? (double)completedCount / episodes.Count * 100
                    : 0;

                _episodeTabIndex = 0;
                _episodeFilterIndex = 0;
                OnPropertyChanged(nameof(EpisodeTabIndex));
                OnPropertyChanged(nameof(EpisodeFilterIndex));
                OnPropertyChanged(nameof(HasSpecialEpisodes));
                OnPropertyChanged(nameof(SpecialEpisodeCount));
                ApplySortOrder();
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(EpisodesEmptyVisibility));
            }

            // Nach dem UI-Refresh: Priorität für die sichtbaren Folgen anstoßen.
            // Fire-and-forget — der Background-Service pausiert seinen Loop selbst,
            // damit die sichtbare Serie das HTTP-/Dateisystem-Kontingent zuerst bekommt.
            StartPriorityLoad(seriesId);
        }

        /// <summary>
        /// Startet den Priority-Cover-Load für die geöffnete Serie im Hintergrund und
        /// legt ein neues <see cref="CancellationTokenSource"/> an, damit das VM bei
        /// Verlassen der Seite (<see cref="CancelPendingPriorityLoad"/>) sauber abbrechen kann.
        /// </summary>
        private void StartPriorityLoad(Guid seriesId)
        {
            if (_backgroundCoverService is null) return;

            _priorityCts = new CancellationTokenSource();
            CancellationToken token = _priorityCts.Token;
            _ = _backgroundCoverService.RequestPriorityForSeriesAsync(seriesId, token);
        }

        /// <summary>
        /// Bricht einen laufenden Priority-Cover-Load ab. Wird beim Verlassen der
        /// Detailseite aus dem <c>OnNavigatedFrom</c>-Pfad der Page aufgerufen, damit
        /// der Hintergrund-Loop nicht unnötig pausiert bleibt.
        /// </summary>
        public void CancelPendingPriorityLoad()
        {
            if (_priorityCts is null) return;

            try
            {
                _priorityCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS wurde bereits abgeräumt – defensiv schlucken.
            }
            _priorityCts.Dispose();
            _priorityCts = null;
        }

        /// <summary>
        /// Wählt eine Episode aus und lädt ihre lokalen Tracks.
        /// </summary>
        /// <param name="episode">Die gewählte Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SelectEpisodeAsync(EpisodeTileViewModel episode)
        {
            ArgumentNullException.ThrowIfNull(episode);
            SelectedEpisode = episode;
            _hasLocalTracks = false;
            Tracks = [];

            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> localTracks = await trackService.GetByEpisodeIdAsync(episode.EpisodeId);
            _hasLocalTracks = localTracks.Count > 0;

            List<LocalTrackRowViewModel> rows = new(localTracks.Count);
            int trackNumber = 1;

            foreach (LocalTrack track in localTracks)
            {
                rows.Add(new LocalTrackRowViewModel(
                    trackId: track.Id,
                    trackNumber: trackNumber++,
                    filePath: track.FilePath,
                    duration: track.Duration,
                    requestTagManagerNavigation: _ => { }));
            }

            Tracks = rows;

            // Sichtbarkeiten für Track-Spalte aktualisieren
            OnPropertyChanged(nameof(TracksEmptyVisibility));
            OnPropertyChanged(nameof(NoLocalTracksVisibility));
            OnPropertyChanged(nameof(TrackActionsVisibility));
        }

        /// <summary>
        /// Startet die Wiedergabe aller Tracks der aktuell gewählten Episode.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public Task PlaySelectedEpisodeAsync()
        {
            if (_selectedEpisode is null)
            {
                return Task.CompletedTask;
            }

            return PlayEpisodeAsync(_selectedEpisode.EpisodeId);
        }

        /// <summary>
        /// Wechselt den Favoritenstatus der aktuellen Serie und persistiert die Änderung.
        /// Existiert noch keine geladene Serie (Guid.Empty), wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        private async Task ToggleFavoriteAsync()
        {
            if (_seriesId == Guid.Empty)
            {
                return;
            }

            bool newValue = !_isFavorite;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            await seriesService.SetFavoriteAsync(_seriesId, newValue);
            IsFavorite = newValue;
        }

        // ── Kontextmenü-Aktionen ─────────────────────────────────────────────────

        /// <summary>
        /// Markiert eine Episode als gehört. Erstellt oder aktualisiert den PlaybackState
        /// und lädt die Kachelliste neu, damit Haken und Fortschritt sofort stimmen.
        /// </summary>
        /// <param name="episodeId">ID der zu markierenden Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task MarkAsPlayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            PlaybackState? existing = await stateService.GetByEpisodeIdAsync(episodeId);

            if (existing is not null)
            {
                existing.IsCompleted = true;
                existing.CompletedAt = _clock.UtcNow;
                await stateService.UpdateAsync(existing);
            }
            else
            {
                PlaybackState newState = new()
                {
                    EpisodeId = episodeId,
                    IsCompleted = true,
                    CompletedAt = _clock.UtcNow,
                    LastPlayedAt = _clock.UtcNow
                };
                await stateService.AddAsync(newState);
            }

            await LoadAsync(_seriesId);
        }

        /// <summary>
        /// Setzt den Wiedergabestatus einer Episode zurück (als ungehört markieren).
        /// Entfernt den gespeicherten PlaybackState und lädt die Kachelliste neu.
        /// </summary>
        /// <param name="episodeId">ID der zurückzusetzenden Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task MarkAsUnplayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            PlaybackState? existing = await stateService.GetByEpisodeIdAsync(episodeId);

            if (existing is not null)
            {
                await stateService.DeleteAsync(existing.Id);
            }

            await LoadAsync(_seriesId);
        }

        // ── Interne Hilfsmethoden ─────────────────────────────────────────────────

        /// <summary>
        /// Startet die Wiedergabe einer Episode.
        /// Lädt die lokalen Tracks und gibt sie an den <see cref="PlayerService"/> weiter.
        /// Existiert ein gespeicherter Fortschritt, wird die Wiedergabe dort fortgesetzt.
        /// </summary>
        /// <param name="episodeId">ID der abzuspielenden Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task PlayEpisodeAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episodeId);

            if (tracks.Count == 0)
            {
                return;
            }

            PlaybackState? savedState = await playbackService.GetByEpisodeIdAsync(episodeId);
            TimeSpan resumePosition = savedState is { IsCompleted: false } ? savedState.LastPosition : TimeSpan.Zero;

            List<string> paths = new(tracks.Count);

            foreach (LocalTrack track in tracks)
            {
                paths.Add(track.FilePath);
            }

            _playerService.Play(episodeId, paths, startIndex: 0, resumePosition: resumePosition);
        }

        /// <summary>
        /// Sortiert <see cref="_allEpisodes"/> nach dem aktuellen <see cref="SortOrder"/>
        /// und aktualisiert <see cref="Episodes"/>.
        /// </summary>
        private void ApplySortOrder()
        {
            // Schritt 0: Tab-Filter
            IEnumerable<EpisodeTileViewModel> tabFiltered = _episodeTabIndex == 1
                ? _allEpisodes.Where(e => e.IsSpecialEpisode)
                : _allEpisodes.Where(e => !e.IsSpecialEpisode);

            // Schritt 1: Status-Filter
            IEnumerable<EpisodeTileViewModel> filtered = _episodeFilterIndex switch
            {
                1 => tabFiltered.Where(e => e.Progress == PlaybackStatus.NotStarted),
                2 => tabFiltered.Where(e => e.Progress == PlaybackStatus.Finished),
                3 => tabFiltered.Where(e => e.Progress == PlaybackStatus.InProgress),
                _ => tabFiltered
            };

            // Schritt 2: Sortieren
            IEnumerable<EpisodeTileViewModel> sorted = _sortOrder switch
            {
                EpisodeSortOrder.Title => filtered.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
                EpisodeSortOrder.ReleaseDate => filtered
                    .OrderBy(e => e.ReleaseDate.HasValue ? 0 : 1)
                    .ThenBy(e => e.ReleaseDate),
                _ => filtered
                    .OrderBy(e => e.EpisodeNumber.HasValue ? 0 : 1)
                    .ThenBy(e => e.EpisodeNumber)
            };

            Episodes = sorted.ToList();
        }

        /// <summary>
        /// Leitet den <see cref="PlaybackStatus"/> aus dem gespeicherten Zustand ab.
        /// </summary>
        private static PlaybackStatus DetermineStatus(PlaybackState? state)
        {
            if (state is null || state.LastPosition == TimeSpan.Zero)
            {
                return PlaybackStatus.NotStarted;
            }

            return state.IsCompleted ? PlaybackStatus.Finished : PlaybackStatus.InProgress;
        }

        // ── Cover-Ladelogik ──────────────────────────────────────────────────────

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Episode.
        /// Priorität: DB-Cover → cover.jpg im Folgenordner → ID3-Tag des ersten Tracks → null.
        /// Nutzt denselben <see cref="EchoPlay.LocalLibrary.Cover.ILocalCoverLoader"/> wie die Mediathek,
        /// damit Cover überall konsistent angezeigt werden.
        /// </summary>
        /// <param name="episode">Die Episode deren Cover geladen wird.</param>
        /// <returns>Das geladene Cover oder null wenn keins vorhanden.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Aufbau pro Episode: DB-/IO-/Bild-Dekodier-Fehler einer einzelnen Episode dürfen die Detailansicht nicht stören – 'null' führt zum Serien-Fallback-Cover.")]
        private async Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode)
        {
            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetEpisodeCoverImageAsync(episode.Id);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            // Dateisystem-Cover über den CoverLoader (cover.jpg oder ID3-Tag)
            if (episode.LocalFolderPath is not null)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    EchoPlay.LocalLibrary.Cover.ILocalCoverLoader coverLoader =
                        scope.ServiceProvider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>();

                    // ID3-Fallback nur wenn kein cover.jpg vorhanden – spart DB-Abfrage
                    string? firstTrackPath = null;
                    if (!File.Exists(Path.Combine(episode.LocalFolderPath, Core.CoverConstants.CoverFileName)))
                    {
                        ILocalTrackDataService trackService =
                            scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                        IReadOnlyList<LocalTrack> tracks =
                            await trackService.GetByEpisodeIdAsync(episode.Id);
                        firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                    if (coverBytes is not null)
                    {
                        return await CoverService.ConvertToBitmapAsync(coverBytes);
                    }
                }
                catch (Exception)
                {
                    // Cover-Laden darf die Ansicht nicht blockieren –
                    // korrupte Bilder oder fehlende Dateien sind kein Abbruchgrund.
                    // Kein Logger im ViewModel verfügbar (bewusste Entscheidung: schlanke VMs).
                }
            }

            return null;
        }

        /// <summary>
        /// Erstellt ein Cover-Bild aus den Seriendaten als Fallback.
        /// Priorität: DB-Cover (CoverService) → cover.jpg im Serienordner → URL → null.
        /// </summary>
        /// <param name="series">Die Serie deren Cover als Fallback dient.</param>
        /// <returns>Das geladene Cover oder null.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fallback-Serien-Cover-Aufbau: DB-/IO-/Bild-Dekodier-Fehler dürfen die Detailansicht nicht stören – 'null' führt zum Platzhalter-Cover.")]
        private async Task<BitmapImage?> BuildSeriesCoverAsync(Series? series)
        {
            if (series is null)
            {
                return null;
            }

            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetSeriesCoverImageAsync(series.Id);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            if (series.LocalFolderPath is not null)
            {
                string coverPath = Path.Combine(series.LocalFolderPath, Core.CoverConstants.CoverFileName);
                if (File.Exists(coverPath))
                {
                    try
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(coverPath);
                        return await CoverService.ConvertToBitmapAsync(bytes);
                    }
                    catch
                    {
                        // Datei-Zugriffsfehler still ignorieren
                    }
                }
            }

            if (series.CoverImageUrl is not null)
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            return null;
        }

    }
}
