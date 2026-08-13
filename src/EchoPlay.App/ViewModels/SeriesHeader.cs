using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Kopfzeile der Serienansicht: Titel, Beschreibung, Favoritenstern und der
    /// Gesamtfortschritt über alle Folgen.
    /// </summary>
    /// <remarks>
    /// Alles hier gehört zur Serie als Ganzes und wird bei jedem Ladelauf gemeinsam
    /// gesetzt — anders als die Folgenliste darunter, die auf jede Filteränderung reagiert.
    /// </remarks>
    public sealed class SeriesHeader : ObservableObject
    {
        /// <summary>Zeichen des gefüllten Favoritensterns.</summary>
        private const string FilledStarGlyph = "";

        /// <summary>Zeichen des leeren Favoritensterns.</summary>
        private const string EmptyStarGlyph = "";

        private readonly IServiceScopeFactory _scopeFactory;

        private string _seriesTitle = string.Empty;
        private string _seriesDescription = string.Empty;
        private Guid _seriesId;
        private bool _isFavorite;
        private string _progressText = string.Empty;
        private double _overallProgressPercent;

        /// <summary>
        /// Initialisiert die Kopfzeile.
        /// </summary>
        /// <param name="scopeFactory">Für das Speichern des Favoritenstatus.</param>
        /// <param name="cancellationToken">Gilt für das Speichern; endet mit der Seite.</param>
        internal SeriesHeader(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
        {
            _scopeFactory = scopeFactory;
            ToggleFavoriteCommand = new RelayCommand(() => _ = ToggleFavoriteAsync(cancellationToken));
        }

        /// <summary>Titel der aktuell angezeigten Serie.</summary>
        public string SeriesTitle
        {
            get => _seriesTitle;
            private set => SetProperty(ref _seriesTitle, value);
        }

        /// <summary>Beschreibungstext der aktuell angezeigten Serie.</summary>
        public string SeriesDescription
        {
            get => _seriesDescription;
            private set => SetProperty(ref _seriesDescription, value);
        }

        /// <summary>Sichtbarkeit des Beschreibungstexts — ohne Text bleibt der Platz leer.</summary>
        public Visibility DescriptionVisibility =>
            string.IsNullOrWhiteSpace(_seriesDescription) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Ob die aktuelle Serie als Favorit gekennzeichnet ist.</summary>
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

        /// <summary>Schaltet den Favoritenstatus der angezeigten Serie um.</summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>Das Zeichen des Favoritensterns: gefüllt, wenn die Serie ein Favorit ist.</summary>
        public string FavoriteGlyph => _isFavorite ? FilledStarGlyph : EmptyStarGlyph;

        /// <summary>Gesamtfortschritts-Text, z.B. "42 von 229 Folgen gehört".</summary>
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        /// <summary>Gesamtfortschritt in Prozent (0–100) für den Fortschrittsbalken.</summary>
        public double OverallProgressPercent
        {
            get => _overallProgressPercent;
            private set => SetProperty(ref _overallProgressPercent, value);
        }

        /// <summary>
        /// Übernimmt Titel, Beschreibung, Favoritenstatus und Fortschritt einer neu
        /// geladenen Serie.
        /// </summary>
        /// <param name="seriesId">Die Kennung der Serie; nötig zum Speichern des Favoriten.</param>
        /// <param name="data">Das Ergebnis des Ladelaufs.</param>
        internal void Apply(Guid seriesId, SeriesDetailData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            _seriesId = seriesId;
            Series? series = data.Series;

            SeriesTitle = series?.Title ?? string.Empty;
            SeriesDescription = series?.Description ?? string.Empty;
            IsFavorite = series?.IsFavorite ?? false;
            OnPropertyChanged(nameof(DescriptionVisibility));

            ProgressText = data.ProgressText;
            OverallProgressPercent = data.ProgressPercent;
        }

        /// <summary>
        /// Wechselt den Favoritenstatus und speichert die Änderung. Ohne geladene Serie
        /// geschieht nichts.
        /// </summary>
        private async Task ToggleFavoriteAsync(CancellationToken cancellationToken)
        {
            if (_seriesId == Guid.Empty)
            {
                return;
            }

            using IDisposable userAction = UserActionScope.BeginUserAction("SeriesToggleFavorite");

            bool newValue = !_isFavorite;
            await SeriesFavoriteToggle.SetFavoriteAsync(_scopeFactory, _seriesId, newValue, cancellationToken);
            IsFavorite = newValue;
        }
    }
}
