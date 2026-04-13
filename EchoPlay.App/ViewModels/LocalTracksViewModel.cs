using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Track-Spalte der lokalen Mediathek. Hält die Trackliste
    /// der aktuell gewählten Episode, die Wiedergabe-Steuerung und die Tag-Manager-Sprünge
    /// für ganze Serien- bzw. Episoden-Ordner. Wird vom <see cref="MediathekLokalViewModel"/>
    /// als Pass-Through-Ziel eingebunden, damit bestehende XAML-Bindings unverändert funktionieren.
    /// </summary>
    public sealed class LocalTracksViewModel : ObservableObject
    {
        private readonly IPlayerService _playerService;
        private readonly Action<string> _requestTagManagerNavigation;

        private IReadOnlyList<LocalTrackRowViewModel> _tracks = [];
        private LocalEpisodeCardViewModel? _selectedEpisode;
        private LocalArtistCardViewModel? _selectedArtist;

        /// <summary>
        /// Initialisiert das Sub-ViewModel.
        /// </summary>
        /// <param name="playerService">Wiedergabe-Service für die Track-Liste.</param>
        /// <param name="requestTagManagerNavigation">Callback an die Top-VM, der die Tag-Manager-Navigation auslöst.</param>
        public LocalTracksViewModel(
            IPlayerService playerService,
            Action<string> requestTagManagerNavigation)
        {
            _playerService               = playerService;
            _requestTagManagerNavigation = requestTagManagerNavigation;

            OpenAllSeriesTracksCommand  = new RelayCommand(() => OpenAllTracksByPath(_selectedArtist?.LocalFolderPath));
            OpenAllEpisodeTracksCommand = new RelayCommand(() => OpenAllTracksByPath(_selectedEpisode?.FolderPath));

            // PlayEpisode ist initial inaktiv – wird per SetEnabled aktiviert sobald Tracks geladen sind.
            PlayEpisodeCommand = new RelayCommand(PlayCurrentEpisode);
            PlayEpisodeCommand.SetEnabled(false);
        }

        /// <summary>Tracks der gewählten Episode – rechte Spalte der Mediathek.</summary>
        public IReadOnlyList<LocalTrackRowViewModel> Tracks
        {
            get => _tracks;
            private set
            {
                if (SetProperty(ref _tracks, value))
                {
                    OnPropertyChanged(nameof(TracksEmptyVisibility));
                    OnPropertyChanged(nameof(TracksHeaderVisibility));
                    PlayEpisodeCommand.SetEnabled(value.Count > 0 && _selectedEpisode is not null);
                }
            }
        }

        /// <summary>Die aktuell ausgewählte Episode (oder <see langword="null"/>).</summary>
        public LocalEpisodeCardViewModel? SelectedEpisode
        {
            get => _selectedEpisode;
            private set
            {
                if (SetProperty(ref _selectedEpisode, value))
                {
                    OnPropertyChanged(nameof(EpisodeAccordionVisibility));
                    OnPropertyChanged(nameof(SelectedEpisodeTitle));
                    PlayEpisodeCommand.SetEnabled(_tracks.Count > 0 && value is not null);
                }
            }
        }

        /// <summary>Der aktuell ausgewählte Künstler – wird für den OpenAllSeriesTracksCommand gebraucht.</summary>
        public LocalArtistCardViewModel? SelectedArtist
        {
            get => _selectedArtist;
            set => SetProperty(ref _selectedArtist, value);
        }

        /// <summary>Sichtbarkeit des "Keine Tracks" Hinweises.</summary>
        public Visibility TracksEmptyVisibility =>
            _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Tracks-Kopfzeile inkl. PlayEpisode-Button.
        /// Nur eingeblendet wenn Tracks geladen sind.
        /// </summary>
        public Visibility TracksHeaderVisibility =>
            _tracks.Count > 0 && _selectedEpisode is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Akkordeon-Bereichs der gewählten Episode.</summary>
        public Visibility EpisodeAccordionVisibility =>
            _selectedEpisode is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Titel der gewählten Episode.</summary>
        public string SelectedEpisodeTitle => _selectedEpisode?.Title ?? string.Empty;

        /// <summary>
        /// Untertitel des Track-Panels: "Folge 229 · 8 Tracks · 1:12:34" oder ohne Folgennummer.
        /// </summary>
        public string TrackPanelSubtitle { get; private set; } = string.Empty;

        /// <summary>Öffnet alle Tracks der aktuell gewählten Serie im Tag-Manager.</summary>
        public ICommand OpenAllSeriesTracksCommand { get; }

        /// <summary>Öffnet alle Tracks der aktuell gewählten Episode im Tag-Manager.</summary>
        public ICommand OpenAllEpisodeTracksCommand { get; }

        /// <summary>Spielt die Tracks der aktuellen Folge in sortierter Reihenfolge ab.</summary>
        public RelayCommand PlayEpisodeCommand { get; }

        /// <summary>
        /// Übernimmt die geladenen Tracks für die übergebene Episode und aktualisiert die UI.
        /// </summary>
        /// <param name="episode">Die ausgewählte Episode.</param>
        /// <param name="tracks">Die rohen Tracks aus der DB (sortiert oder unsortiert).</param>
        public void SetTracks(LocalEpisodeCardViewModel episode, IReadOnlyList<LocalTrack> tracks)
        {
            ArgumentNullException.ThrowIfNull(tracks);
            ArgumentNullException.ThrowIfNull(episode);
            SelectedEpisode = episode;

            List<LocalTrackRowViewModel> trackRows = [];
            TimeSpan totalDuration = TimeSpan.Zero;

            foreach (LocalTrack track in tracks.OrderBy(t => t.TrackNumber))
            {
                trackRows.Add(new LocalTrackRowViewModel(
                    trackId:                     track.Id,
                    trackNumber:                 track.TrackNumber,
                    filePath:                    track.FilePath,
                    duration:                    track.Duration,
                    requestTagManagerNavigation: _requestTagManagerNavigation));

                totalDuration += track.Duration;
            }

            Tracks = trackRows;

            string durationText = totalDuration.TotalHours >= 1
                ? $"{(int)totalDuration.TotalHours}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}"
                : $"{(int)totalDuration.TotalMinutes}:{totalDuration.Seconds:D2}";

            string trackWord = tracks.Count == 1 ? "Track" : "Tracks";
            string numberPart = episode.EpisodeNumber.HasValue
                ? $"Folge {episode.EpisodeNumber.Value} \u00B7 "
                : string.Empty;

            TrackPanelSubtitle = $"{numberPart}{tracks.Count} {trackWord} \u00B7 {durationText}";
            OnPropertyChanged(nameof(TrackPanelSubtitle));
        }

        /// <summary>Setzt die Track-Auswahl zurück (kein Episode-Bereich, keine Tracks).</summary>
        public void Clear()
        {
            SelectedEpisode = null;
            Tracks          = [];
        }

        /// <summary>
        /// Spielt die Tracks der aktuell gewählten Episode in korrekter Reihenfolge ab.
        /// Stille Rückkehr wenn keine Episode/keine Tracks vorhanden sind.
        /// </summary>
        private void PlayCurrentEpisode()
        {
            if (_selectedEpisode is null || _tracks.Count == 0)
            {
                return;
            }

            List<string> trackPaths = [.. _tracks.Select(t => t.FilePath)];
            _playerService.Play(_selectedEpisode.EpisodeId, trackPaths);
        }

        /// <summary>
        /// Löst die Tag-Manager-Navigation aus, wenn ein gültiger Pfad vorhanden ist.
        /// </summary>
        private void OpenAllTracksByPath(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _requestTagManagerNavigation(path);
            }
        }
    }
}
