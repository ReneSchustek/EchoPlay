using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Hörspielserie in der linken Spalte der lokalen Mediathek.
    /// Nur Serien mit einem lokal zugeordneten Ordner (<see cref="LocalFolderPath"/> != null) werden hier angezeigt.
    /// </summary>
    /// <remarks>
    /// Erweitert <see cref="SeriesTileViewModelBase"/>, damit <see cref="CoverCardViewModelBase.CoverImage"/> und <see cref="SeriesTileViewModelBase.IsFavorite"/>
    /// nachträglich geändert werden können, ohne die Liste neu aufzubauen. Das ViewModel der lokalen Mediathek
    /// zeigt die Serien-Kacheln sofort an, Cover laden progressiv im Hintergrund nach.
    /// </remarks>
    public sealed class LocalArtistCardViewModel : SeriesTileViewModelBase
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Erstellt ein Kachel-ViewModel für die linke Spalte.
        /// </summary>
        /// <param name="data">Was die Kachel anzeigt.</param>
        /// <param name="coverImage">Vorschaubild oder null wenn kein Cover vorhanden ist.</param>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe beim Favoriten-Toggle.</param>
        public LocalArtistCardViewModel(
            LocalArtistCardData data,
            BitmapImage? coverImage,
            IServiceScopeFactory scopeFactory)
        {
            ArgumentNullException.ThrowIfNull(data);
            SeriesId = data.SeriesId;
            Title = data.Title;
            CoverImage = coverImage;
            LocalFolderPath = data.LocalFolderPath;
            LocalEpisodeCount = data.LocalEpisodeCount;
            TotalEpisodeCount = data.TotalEpisodeCount;
            IsFavorite = data.IsFavorite;
            IsWatched = data.IsWatched;
            IsIncomplete = data.IsIncomplete;
            _scopeFactory = scopeFactory;

            ToggleFavoriteCommand = new RelayCommand(() => _ = ToggleFavoriteAsync());
        }

        /// <summary>Datenbank-ID der Serie.</summary>
        public Guid SeriesId { get; }

        /// <summary>Titel der Serie.</summary>
        public string Title { get; }

        /// <summary>
        /// Pfad zum lokalen Serienordner.
        /// Wird beim "Alle Tracks dieser Serie"-Button als Argument für den Tag-Manager übergeben.
        /// </summary>
        public string? LocalFolderPath { get; }

        private int _localEpisodeCount;
        private int _totalEpisodeCount;

        /// <summary>Anzahl der Episoden mit mindestens einem lokal gefundenen Track.</summary>
        public int LocalEpisodeCount
        {
            get => _localEpisodeCount;
            private set => SetProperty(ref _localEpisodeCount, value);
        }

        /// <summary>Gesamtanzahl aller Episoden dieser Serie.</summary>
        public int TotalEpisodeCount
        {
            get => _totalEpisodeCount;
            private set => SetProperty(ref _totalEpisodeCount, value);
        }

        /// <summary>
        /// Anzeige-Text der Episodenzähler, z.B. "3 / 10".
        /// Zeigt wie viele Episoden lokal vorhanden sind gegenüber der Gesamtanzahl.
        /// </summary>
        public string CountText => $"{LocalEpisodeCount} / {TotalEpisodeCount}";

        /// <summary>
        /// Ob der Serie Folgen fehlen: eine Lücke in der Nummernfolge oder eine Folge, die der
        /// Anbieter nach der letzten lokalen schon geliefert hat. Die Folgenzähler taugen dafür
        /// nicht, denn eine lokale Serie kennt nur die Folgen ihrer eigenen Ordner.
        /// </summary>
        public bool IsIncomplete { get; }

        /// <summary>
        /// Aktualisiert die Episodenzähler nachträglich, z.B. wenn der Scan die Episoden einer
        /// Serie erst nach dem Anlegen der Kachel persistiert. Meldet zusätzlich <see cref="CountText"/>
        /// als geändert, damit die gebundene Anzeige live nachzieht statt auf "0 / 0" zu verharren.
        /// </summary>
        /// <param name="localEpisodeCount">Neue Anzahl lokal vorhandener Episoden.</param>
        /// <param name="totalEpisodeCount">Neue Gesamtanzahl der Episoden.</param>
        public void UpdateCounts(int localEpisodeCount, int totalEpisodeCount)
        {
            LocalEpisodeCount = localEpisodeCount;
            TotalEpisodeCount = totalEpisodeCount;
            OnPropertyChanged(nameof(CountText));
        }

        /// <summary>
        /// Schaltet den Favoritenstatus der Serie um und persistiert die Änderung in der Datenbank.
        /// </summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>
        /// Schaltet den Favoritenstatus um: Favorit → kein Favorit, kein Favorit → Favorit.
        /// Speichert den neuen Status sofort in der Datenbank.
        /// </summary>
        private async System.Threading.Tasks.Task ToggleFavoriteAsync()
        {
            bool newValue = !IsFavorite;

            await SeriesFavoriteToggle.SetFavoriteAsync(_scopeFactory, SeriesId, newValue);
            IsFavorite = newValue;

            // Favorisieren aktiviert in der Datenschicht zugleich die Überwachung.
            // Ohne diese Zeile bliebe das Auge auf der Kachel bis zum nächsten Laden aus.
            if (newValue)
            {
                IsWatched = true;
            }
        }
    }
}
