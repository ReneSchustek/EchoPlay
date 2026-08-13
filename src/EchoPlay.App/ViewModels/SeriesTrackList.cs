using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Spurenspalte der Serienansicht: welche Folge gewählt ist und welche Dateien
    /// dazu vorliegen.
    /// </summary>
    /// <remarks>
    /// Derselbe Bereich wie das <see cref="LocalTracksViewModel"/> der Mediathek, nur ohne
    /// Sprung in den Tag-Manager — die Serienansicht bietet ihn dort nicht an. Die drei
    /// Hinweise („Folge wählen", „keine Dateien", Aktionsleiste) hängen alle an derselben
    /// Auswahl und stehen deshalb hier statt verteilt im Ansichtsmodell der Seite.
    /// </remarks>
    public sealed class SeriesTrackList : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private IReadOnlyList<LocalTrackRowViewModel> _tracks = [];
        private EpisodeTileViewModel? _selectedEpisode;
        private bool _hasLocalTracks;

        /// <summary>
        /// Initialisiert die Spurenspalte.
        /// </summary>
        /// <param name="scopeFactory">Für den Zugriff auf die Spuren einer Folge.</param>
        internal SeriesTrackList(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Die lokalen Spuren der gewählten Folge.</summary>
        public IReadOnlyList<LocalTrackRowViewModel> Tracks
        {
            get => _tracks;
            private set
            {
                if (SetProperty(ref _tracks, value))
                {
                    OnPropertyChanged(nameof(TrackActionsVisibility));
                }
            }
        }

        /// <summary>Die aktuell gewählte Folge, oder <see langword="null"/>.</summary>
        public EpisodeTileViewModel? SelectedEpisode
        {
            get => _selectedEpisode;
            private set => SetProperty(ref _selectedEpisode, value);
        }

        /// <summary>
        /// Sichtbarkeit des „Folge wählen"-Hinweises. Eingeblendet, solange keine Folge
        /// gewählt wurde.
        /// </summary>
        public Visibility TracksEmptyVisibility =>
            _selectedEpisode is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des „Keine lokalen Dateien"-Hinweises. Eingeblendet, wenn eine Folge
        /// gewählt ist, zu der nichts auf der Platte liegt.
        /// </summary>
        public Visibility NoLocalTracksVisibility =>
            _selectedEpisode is not null && !_hasLocalTracks ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Aktionsleiste („Ganze Folge abspielen"). Nur sichtbar, wenn
        /// Spuren geladen sind.
        /// </summary>
        public Visibility TrackActionsVisibility =>
            _tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Wählt eine Folge und lädt ihre lokalen Spuren. Die Titel aus der Kennzeichnung
        /// werden danach im Hintergrund nachgetragen — die Liste steht sofort.
        /// </summary>
        /// <param name="episode">Die gewählte Folge.</param>
        /// <param name="cancellationToken">Bricht das Laden ab.</param>
        public async Task SelectEpisodeAsync(EpisodeTileViewModel episode, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episode);

            SelectedEpisode = episode;
            _hasLocalTracks = false;
            Tracks = [];

            List<LocalTrackRowViewModel> rows = await LoadRowsAsync(episode.EpisodeId, cancellationToken);
            _hasLocalTracks = rows.Count > 0;
            Tracks = rows;

            OnPropertyChanged(nameof(TracksEmptyVisibility));
            OnPropertyChanged(nameof(NoLocalTracksVisibility));
            OnPropertyChanged(nameof(TrackActionsVisibility));

            await TrackTitleFiller.FillAsync(_scopeFactory, rows, cancellationToken);
        }

        /// <summary>Leert die Spalte — beim Wechsel der Serie.</summary>
        public void Clear()
        {
            SelectedEpisode = null;
            _hasLocalTracks = false;
            Tracks = [];

            OnPropertyChanged(nameof(TracksEmptyVisibility));
            OnPropertyChanged(nameof(NoLocalTracksVisibility));
        }

        /// <summary>Liest die lokalen Spuren einer Folge und baut ihre Zeilen.</summary>
        private async Task<List<LocalTrackRowViewModel>> LoadRowsAsync(Guid episodeId, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> localTracks = await trackService.GetByEpisodeIdAsync(episodeId, cancellationToken);
            List<LocalTrackRowViewModel> rows = new(localTracks.Count);
            int trackNumber = 1;

            foreach (LocalTrack track in localTracks)
            {
                rows.Add(new LocalTrackRowViewModel(
                    trackId: track.Id,
                    trackNumber: trackNumber++,
                    filePath: track.FilePath,
                    duration: track.Duration,
                    // Die Serienansicht bietet den Sprung in den Tag-Manager nicht an.
                    requestTagManagerNavigation: _ => { }));
            }

            return rows;
        }
    }
}
