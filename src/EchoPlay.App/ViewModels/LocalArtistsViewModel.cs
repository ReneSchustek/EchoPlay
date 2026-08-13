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
    /// Liste der lokalen Serienkacheln, den clientseitigen Suchfilter und die Auswahl.
    /// Woher die Kacheln kommen, beantwortet <see cref="LocalArtistCardFactory"/>; woher
    /// ihre Cover kommen, <see cref="ISeriesCoverBuilder"/>.
    /// Wird vom <see cref="LocalLibraryViewModel"/> als Pass-Through-Ziel eingebunden,
    /// damit bestehende XAML-Bindings unverändert funktionieren.
    /// </summary>
    public sealed class LocalArtistsViewModel : ObservableObject
    {
        private readonly LocalArtistCardFactory _cardFactory;
        private readonly LocalArtistFilter _filter;

        private List<LocalArtistCardViewModel> _allArtists = [];
        private IReadOnlyList<LocalArtistCardViewModel> _artists = [];
        private LocalArtistCardViewModel? _selectedArtist;
        private int _selectedArtistIndex = -1;

        /// <summary>
        /// Initialisiert das Sub-ViewModel.
        /// </summary>
        /// <param name="scopeFactory">Für die Datenbankzugriffe beim Laden.</param>
        /// <param name="coverBuilder">Beschafft die Cover. Ohne ihn bleiben die Kacheln bildlos.</param>
        /// <param name="filter">
        /// Die Filterkriterien. Wird eine Ablage übergeben, überlebt der Filter den Wechsel auf
        /// eine andere Seite; ohne Angabe gilt er nur für dieses ViewModel.
        /// </param>
        public LocalArtistsViewModel(
            IServiceScopeFactory scopeFactory,
            ISeriesCoverBuilder? coverBuilder = null,
            LocalArtistFilter? filter = null)
        {
            _cardFactory = new LocalArtistCardFactory(scopeFactory, coverBuilder);
            _filter = filter ?? new LocalArtistFilter();
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
                    OnPropertyChanged(nameof(NoResultsVisibility));
                }
            }
        }

        /// <summary>
        /// Freitext-Suchfilter für die Serien-Kacheln. Filtert clientseitig auf Titel –
        /// kein neuer DB-Query. Leerer String zeigt alle Serien.
        /// </summary>
        public string LocalSearchText
        {
            get => _filter.SearchText;
            set => SetFilter(value, f => f.SearchText = value, nameof(LocalSearchText), _filter.SearchText);
        }

        /// <summary>Zeigt nur Serien, die als Favorit markiert sind.</summary>
        public bool FavoritesOnly
        {
            get => _filter.FavoritesOnly;
            set => SetFilter(value, f => f.FavoritesOnly = value, nameof(FavoritesOnly), _filter.FavoritesOnly);
        }

        /// <summary>Zeigt nur Serien, die auf neue Folgen geprüft werden.</summary>
        public bool WatchedOnly
        {
            get => _filter.WatchedOnly;
            set => SetFilter(value, f => f.WatchedOnly = value, nameof(WatchedOnly), _filter.WatchedOnly);
        }

        /// <summary>Zeigt nur Serien, denen lokal Folgen fehlen.</summary>
        public bool IncompleteOnly
        {
            get => _filter.IncompleteOnly;
            set => SetFilter(value, f => f.IncompleteOnly = value, nameof(IncompleteOnly), _filter.IncompleteOnly);
        }

        /// <summary>
        /// Übernimmt ein geändertes Filterkriterium, meldet es und wendet den Filter neu an.
        /// Unveränderte Werte lösen nichts aus — sonst baute jede Zuweisung die Liste neu auf.
        /// </summary>
        private void SetFilter<T>(T neu, Action<LocalArtistFilter> assign, string name, T alt)
        {
            if (EqualityComparer<T>.Default.Equals(alt, neu))
            {
                return;
            }

            assign(_filter);
            OnPropertyChanged(name);
            ApplyFilters();
        }

        /// <summary>Ob Suche oder Filter die Sicht gerade einschränken.</summary>
        public bool HasActiveFilter => _filter.IsActive;

        /// <summary>
        /// Nimmt Suche und Filter zurück. Nach einem Treffer ohne Ergebnis führt das den
        /// Nutzer in einem Schritt zum vollständigen Bestand zurück.
        /// </summary>
        public void ResetFilters()
        {
            _filter.Reset();

            OnPropertyChanged(nameof(LocalSearchText));
            OnPropertyChanged(nameof(FavoritesOnly));
            OnPropertyChanged(nameof(WatchedOnly));
            OnPropertyChanged(nameof(IncompleteOnly));

            ApplyFilters();
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
        /// Sichtbarkeit des „Noch keine Serien"-Platzhalters – erscheint nur, wenn die
        /// Bibliothek wirklich leer ist.
        /// </summary>
        public Visibility ArtistsEmptyVisibility =>
            _allArtists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des „Nichts gefunden"-Platzhalters – erscheint, wenn der Bestand
        /// gefüllt ist und erst Suche oder Filter ihn leer räumen.
        /// <para>
        /// Die Unterscheidung zum leeren Bestand ist der eigentliche Punkt: „Nichts da" führt
        /// zum Einlesen der Bibliothek, „nichts gefunden" zum Zurücknehmen der Suche. Ein
        /// gemeinsamer Text schickte die Hälfte der Nutzer in die falsche Richtung.
        /// </para>
        /// </summary>
        public Visibility NoResultsVisibility =>
            _allArtists.Count > 0 && _artists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            IReadOnlyList<LocalArtistCardViewModel> cards = await _cardFactory.CreateAllAsync();

            // Auswahl zurücksetzen, dann Liste übernehmen
            SelectedArtist = null;
            SelectedArtistIndex = -1;
            _allArtists = [.. cards];
            ApplyFilters();
        }

        /// <summary>
        /// Fügt eine vom Sync-Service gemeldete Serie sofort zur Künstlerliste hinzu.
        /// Wird als Callback aus dem Scan-Event aufgerufen. Duplikat-Schutz verhindert
        /// doppelte Kacheln, wenn LoadFromDatabaseAsync und das Event dieselbe Serie melden.
        /// </summary>
        public async Task AppendArtistCardAsync(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);
            try
            {
                // Karte existiert bereits (z.B. früher im Scan mit noch 0 Folgen gemeldet):
                // Zähler live nachziehen statt abzubrechen – sonst bleibt die Kachel auf "0 / 0"
                // stehen, bis erst der abschließende Neuaufbau sie ersetzt.
                LocalArtistCardViewModel? existing = _allArtists.FirstOrDefault(a => a.SeriesId == series.Id);

                if (existing is not null)
                {
                    (int localCount, int totalCount) = await _cardFactory.CountEpisodesAsync(series.Id);
                    existing.UpdateCounts(localCount, totalCount);

                    // Eine im laufenden Einlesen angelegte Karte kann noch ohne Cover sein: keine
                    // cover.jpg zur Anlagezeit, das Bild aus der Datenbank schreibt der
                    // Hintergrunddienst erst später. Ohne dieses Nachziehen bliebe die Serie die
                    // ganze Sitzung ohne Bild.
                    await _cardFactory.FillMissingCoverAsync(existing, series);
                    return;
                }

                _allArtists = [.. _allArtists, await _cardFactory.CreateAsync(series)];
                ApplyFilters();
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

            SelectedArtist = null;
            SelectedArtistIndex = -1;
        }

        /// <summary>
        /// Setzt die aktive Künstler-Auswahl und ermittelt den Index in der gefilterten Liste.
        /// </summary>
        /// <param name="artist">Die zu markierende Künstler-Karte.</param>
        public void SelectArtist(LocalArtistCardViewModel artist)
        {
            ArgumentNullException.ThrowIfNull(artist);
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
            _allArtists = [];
            Artists = [];
            SelectedArtist = null;
            SelectedArtistIndex = -1;
        }

        /// <summary>
        /// Wendet Suchtext und Filter auf <see cref="_allArtists"/> an und aktualisiert die
        /// <see cref="Artists"/>-Liste. Ohne Suchtext und ohne Filter stehen alle Serien.
        /// </summary>
        private void ApplyFilters()
        {
            if (!_filter.IsActive)
            {
                Artists = _allArtists;
                OnPropertyChanged(nameof(HasActiveFilter));
                return;
            }

            List<LocalArtistCardViewModel> filtered = [];
            foreach (LocalArtistCardViewModel card in _allArtists)
            {
                if (_filter.Matches(card))
                {
                    filtered.Add(card);
                }
            }

            Artists = filtered;
            OnPropertyChanged(nameof(HasActiveFilter));
        }

    }
}
