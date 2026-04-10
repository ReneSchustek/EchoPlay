using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Künstler-/Serien-Spalte der lokalen Mediathek. Hält die
    /// Liste der lokalen Serienkacheln, den clientseitigen Suchfilter, die Auswahl-State
    /// und die Cover-Aufbau-Logik. Wird vom <see cref="MediathekLokalViewModel"/> als
    /// Pass-Through-Ziel eingebunden, damit bestehende XAML-Bindings unverändert funktionieren.
    /// </summary>
    public sealed class LocalArtistsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CoverService? _coverService;

        private IReadOnlyList<LocalArtistCardViewModel> _allArtists = [];
        private IReadOnlyList<LocalArtistCardViewModel> _artists = [];
        private string _localSearchText = string.Empty;
        private LocalArtistCardViewModel? _selectedArtist;
        private int _selectedArtistIndex = -1;

        /// <summary>
        /// Initialisiert das Sub-ViewModel.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankzugriffe beim Laden und beim Cover-Aufbau.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. In Tests <see langword="null"/>.</param>
        public LocalArtistsViewModel(
            IServiceScopeFactory scopeFactory,
            CoverService? coverService = null)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
        }

        /// <summary>
        /// Serien mit lokalem Ordner – linke Spalte. Gefilterte Sicht auf <see cref="_allArtists"/>.
        /// </summary>
        public IReadOnlyList<LocalArtistCardViewModel> Artists
        {
            get => _artists;
            private set
            {
                if (SetProperty(ref _artists, value))
                {
                    OnPropertyChanged(nameof(ArtistsEmptyVisibility));
                }
            }
        }

        /// <summary>
        /// Freitext-Suchfilter für die Serien-Kacheln. Filtert clientseitig auf Titel –
        /// kein neuer DB-Query. Leerer String zeigt alle Serien.
        /// </summary>
        public string LocalSearchText
        {
            get => _localSearchText;
            set
            {
                if (SetProperty(ref _localSearchText, value))
                {
                    ApplyLocalSearchFilter();
                }
            }
        }

        /// <summary>Aktuell gewählte Serie – steuert die mittlere Spalte.</summary>
        public LocalArtistCardViewModel? SelectedArtist
        {
            get => _selectedArtist;
            set
            {
                if (SetProperty(ref _selectedArtist, value))
                {
                    OnPropertyChanged(nameof(SeriesActionsVisibility));
                }
            }
        }

        /// <summary>
        /// Index der ausgewählten Serie in <see cref="Artists"/>. -1 wenn keine Serie gewählt.
        /// Wird von der Page genutzt, um die Serien-Liste an der richtigen Zeile aufzuteilen.
        /// </summary>
        public int SelectedArtistIndex
        {
            get => _selectedArtistIndex;
            set
            {
                if (SetProperty(ref _selectedArtistIndex, value))
                {
                    OnPropertyChanged(nameof(EpisodesAccordionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des "Keine Serien"-Platzhalters – erscheint wenn die Bibliothek leer ist.
        /// </summary>
        public Visibility ArtistsEmptyVisibility =>
            _artists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Folgen-Akkordeons – eingeblendet sobald eine Serie ausgewählt ist.
        /// </summary>
        public Visibility EpisodesAccordionVisibility =>
            _selectedArtistIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Alle Tracks dieser Serie bearbeiten"-Buttons.
        /// Nur eingeblendet wenn eine Serie mit bekanntem Ordner gewählt ist.
        /// </summary>
        public Visibility SeriesActionsVisibility =>
            _selectedArtist?.LocalFolderPath is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Zugriff auf die ungefilterte Künstler-Liste – z.B. um eine Karte per ID zu finden.</summary>
        public IReadOnlyList<LocalArtistCardViewModel> AllArtists => _allArtists;

        /// <summary>
        /// Lädt alle Serien mit lokalem Ordner aus der Datenbank, baut Karten daraus und
        /// stößt das progressive Cover-Laden im Hintergrund an. Setzt Auswahl und Filter zurück.
        /// </summary>
        public async Task LoadFromDatabaseAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService   = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            // Nur Serien mit lokalem Ordner anzeigen
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            List<Series> localSeries = [.. allSeries.Where(s => s.LocalFolderPath is not null)];

            // Episodenzähler in einer Batch-Abfrage – ersetzt N einzelne GetBySeriesIdAsync-Aufrufe
            List<Guid> seriesIds = [.. localSeries.Select(s => s.Id)];
            IReadOnlyDictionary<Guid, (int Total, int Local)> episodeCounts =
                await episodeService.GetEpisodeCountsForSeriesAsync(seriesIds);

            List<LocalArtistCardViewModel> artistCards = new(localSeries.Count);

            foreach (Series series in localSeries)
            {
                (int total, int local) = episodeCounts.TryGetValue(series.Id, out (int Total, int Local) counts)
                    ? (counts.Total, counts.Local)
                    : (0, 0);

                artistCards.Add(new LocalArtistCardViewModel(
                    seriesId:          series.Id,
                    title:             series.Title,
                    coverImage:        null,
                    localFolderPath:   series.LocalFolderPath,
                    localEpisodeCount: local,
                    totalEpisodeCount: total,
                    isFavorite:        series.IsFavorite,
                    isWatched:         series.IsWatched,
                    scopeFactory:      _scopeFactory));
            }

            // Auswahl zurücksetzen, dann Liste übernehmen
            SelectedArtist      = null;
            SelectedArtistIndex = -1;
            _allArtists         = artistCards;
            ApplyLocalSearchFilter();

            // Cover progressiv im Hintergrund laden – Kacheln sind bereits sichtbar
            foreach ((LocalArtistCardViewModel card, Series series) in artistCards.Zip(localSeries))
            {
                _ = LoadCoverForCardAsync(card, series);
            }
        }

        /// <summary>
        /// Fügt eine vom Sync-Service gemeldete Serie sofort zur Künstlerliste hinzu.
        /// Wird als Callback aus dem Scan-Event aufgerufen. Duplikat-Schutz verhindert
        /// doppelte Kacheln, wenn LoadFromDatabaseAsync und das Event dieselbe Serie melden.
        /// </summary>
        public async Task AppendArtistCardAsync(Series series)
        {
            try
            {
                if (_allArtists.Any(a => a.SeriesId == series.Id))
                {
                    return;
                }

                BitmapImage? cover = await BuildCoverImageAsync(series);

                using IServiceScope scope = _scopeFactory.CreateScope();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);
                int localEpisodeCount = episodes.Count(e => e.LocalFolderPath is not null);
                int totalEpisodeCount = episodes.Count;

                LocalArtistCardViewModel card = new(
                    seriesId:          series.Id,
                    title:             series.Title,
                    coverImage:        cover,
                    localFolderPath:   series.LocalFolderPath,
                    localEpisodeCount: localEpisodeCount,
                    totalEpisodeCount: totalEpisodeCount,
                    isFavorite:        series.IsFavorite,
                    isWatched:         series.IsWatched,
                    scopeFactory:      _scopeFactory);

                _allArtists = [.. _allArtists, card];
                ApplyLocalSearchFilter();
            }
            catch (IOException)
            {
                // Cover-/Datei-Fehler dürfen den Scan nicht unterbrechen
            }
            catch (UnauthorizedAccessException)
            {
                // Keine Lese-/Schreibrechte – Karte erscheint beim abschließenden LoadAsync()
            }
        }

        /// <summary>
        /// Hebt die Serienauswahl auf. Zurücksetzen der Auswahl-State und der V-Indikatoren.
        /// </summary>
        public void DeselectArtist()
        {
            foreach (LocalArtistCardViewModel a in _allArtists)
            {
                a.IsSelectedInAccordion = false;
            }

            SelectedArtist      = null;
            SelectedArtistIndex = -1;
        }

        /// <summary>
        /// Setzt die aktive Künstler-Auswahl und ermittelt den Index in der gefilterten Liste.
        /// </summary>
        /// <param name="artist">Die zu markierende Künstler-Karte.</param>
        public void SelectArtist(LocalArtistCardViewModel artist)
        {
            foreach (LocalArtistCardViewModel a in _allArtists)
            {
                a.IsSelectedInAccordion = false;
            }

            artist.IsSelectedInAccordion = true;
            SelectedArtist = artist;

            int idx = -1;
            for (int i = 0; i < _artists.Count; i++)
            {
                if (ReferenceEquals(_artists[i], artist)) { idx = i; break; }
            }
            SelectedArtistIndex = idx;
        }

        /// <summary>Setzt die Liste komplett zurück (vor Beginn eines neuen Scans).</summary>
        public void Clear()
        {
            _allArtists         = [];
            Artists             = [];
            SelectedArtist      = null;
            SelectedArtistIndex = -1;
        }

        /// <summary>
        /// Filtert <see cref="_allArtists"/> nach dem aktuellen Suchtext und aktualisiert
        /// die <see cref="Artists"/>-Liste. Leerer Suchtext zeigt alle Serien.
        /// </summary>
        private void ApplyLocalSearchFilter()
        {
            if (string.IsNullOrWhiteSpace(_localSearchText))
            {
                Artists = _allArtists;
                return;
            }

            List<LocalArtistCardViewModel> filtered = [];
            foreach (LocalArtistCardViewModel card in _allArtists)
            {
                if (card.Title.Contains(_localSearchText, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(card);
                }
            }

            Artists = filtered;
        }

        /// <summary>
        /// Lädt das Cover einer Serien-Karte im Hintergrund und setzt es. Fehler werden
        /// ignoriert – der Platzhalter bleibt bestehen, ohne den Ladevorgang zu stören.
        /// </summary>
        private async Task LoadCoverForCardAsync(LocalArtistCardViewModel card, Series series)
        {
            try
            {
                BitmapImage? cover = await BuildCoverImageAsync(series);
                card.CoverImage = cover;
            }
            catch (IOException)
            {
                // Cover-Lese-Fehler ignorieren – Platzhalter bleibt
            }
            catch (UnauthorizedAccessException)
            {
                // Keine Leserechte – Platzhalter bleibt
            }
        }

        /// <summary>
        /// Erstellt ein <see cref="BitmapImage"/> für eine Serie. Priorität:
        /// DB-Cover (CoverImages-Tabelle) → cover.jpg im Serienordner → null.
        /// Wird ein lokales Cover gefunden, das noch nicht in der DB liegt, wird es dort
        /// für schnelle Batch-Abfragen gespeichert.
        /// </summary>
        private async Task<BitmapImage?> BuildCoverImageAsync(Series series)
        {
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
                string coverPath = Path.Combine(series.LocalFolderPath, CoverConstants.CoverFileName);
                if (File.Exists(coverPath))
                {
                    byte[] coverBytes = await File.ReadAllBytesAsync(coverPath);

                    if (_coverService is not null && coverBytes.Length > 0)
                    {
                        await _coverService.SetSeriesCoverAsync(series.Id, coverBytes);
                    }

                    return await CoverService.ConvertToBitmapAsync(coverBytes);
                }
            }

            return null;
        }
    }
}
