using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
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
    /// zeigt die Serien-Kacheln sofort an – Cover laden progressiv im Hintergrund nach.
    /// </remarks>
    public sealed class LocalArtistCardViewModel : SeriesTileViewModelBase
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Erstellt ein Kachel-ViewModel für die linke Spalte.
        /// </summary>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
        /// <param name="title">Titel der Serie.</param>
        /// <param name="coverImage">Vorschaubild oder null wenn kein Cover vorhanden ist.</param>
        /// <param name="localFolderPath">Lokaler Serienordner – für den "Alle Tracks"-Button.</param>
        /// <param name="localEpisodeCount">Episoden mit mindestens einem lokal gefundenen Track.</param>
        /// <param name="totalEpisodeCount">Gesamtanzahl aller Episoden dieser Serie.</param>
        /// <param name="isFavorite">Ob die Serie aktuell als Favorit markiert ist.</param>
        /// <param name="isWatched">Ob die Serie auf Neuerscheinungen überwacht wird.</param>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe beim Favoriten-Toggle.</param>
        public LocalArtistCardViewModel(
            Guid seriesId,
            string title,
            BitmapImage? coverImage,
            string? localFolderPath,
            int localEpisodeCount,
            int totalEpisodeCount,
            bool isFavorite,
            bool isWatched,
            IServiceScopeFactory scopeFactory)
        {
            SeriesId = seriesId;
            Title = title;
            CoverImage = coverImage;
            LocalFolderPath = localFolderPath;
            LocalEpisodeCount = localEpisodeCount;
            TotalEpisodeCount = totalEpisodeCount;
            IsFavorite = isFavorite;
            IsWatched = isWatched;
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

        /// <summary>Anzahl der Episoden mit mindestens einem lokal gefundenen Track.</summary>
        public int LocalEpisodeCount { get; }

        /// <summary>Gesamtanzahl aller Episoden dieser Serie.</summary>
        public int TotalEpisodeCount { get; }

        /// <summary>
        /// Anzeige-Text der Episodenzähler, z.B. "3 / 10".
        /// Zeigt wie viele Episoden lokal vorhanden sind gegenüber der Gesamtanzahl.
        /// </summary>
        public string CountText => $"{LocalEpisodeCount} / {TotalEpisodeCount}";

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
        }
    }
}
