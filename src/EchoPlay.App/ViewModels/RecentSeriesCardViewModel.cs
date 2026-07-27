using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für den "Zuletzt gehört"-Abschnitt der Startseite.
    /// Repräsentiert eine Hörspielserie, bei der zuletzt eine Episode gehört wurde.
    /// Dieses ViewModel ist rein lesend – es enthält keine Commands.
    /// </summary>
    public sealed class RecentSeriesCardViewModel
    {
        private readonly EchoPlay.App.Services.ILocalizationService? _localizationService;

        /// <summary>
        /// Initialisiert das ViewModel mit den nötigen Daten.
        /// </summary>
        /// <param name="seriesId">Datenbank-ID der Serie, für die Navigation zu <see cref="Views.SeriesDetailPage"/>.</param>
        /// <param name="seriesName">Titel der Serie.</param>
        /// <param name="lastEpisodeTitle">Titel der zuletzt gehörten Episode.</param>
        /// <param name="coverImage">Coverbild der Serie, oder <see langword="null"/> wenn keines vorhanden ist.</param>
        /// <param name="localizationService">Für den Automation-Namen der Kachel. Nullable für Tests.</param>
        public RecentSeriesCardViewModel(
            Guid seriesId,
            string seriesName,
            string lastEpisodeTitle,
            BitmapImage? coverImage,
            EchoPlay.App.Services.ILocalizationService? localizationService = null)
        {
            _localizationService = localizationService;
            SeriesId = seriesId;
            SeriesName = seriesName;
            LastEpisodeTitle = lastEpisodeTitle;
            CoverImage = coverImage;
        }

        /// <summary>Datenbank-ID der Serie – wird bei der Navigation zur Detailseite übergeben.</summary>
        public Guid SeriesId { get; }

        /// <summary>Titel der Serie, z.B. "Die drei ???".</summary>
        public string SeriesName { get; }

        /// <summary>
        /// Automation-Name der Kachel-Schaltfläche: nennt die Aktion und die Serie.
        /// </summary>
        public string OpenAutomationName => AutomationNameFormatter.Format(
            _localizationService, "TileOpenAutomationName", "Öffnen: {0}", SeriesName);

        /// <summary>
        /// Titel der zuletzt gehörten Episode.
        /// Dient als schneller Wiedereinstieg in die Serie.
        /// </summary>
        public string LastEpisodeTitle { get; }

        /// <summary>
        /// Coverbild der Serie.
        /// <see langword="null"/> wenn weder lokal gespeicherte Bilddaten noch eine Cover-URL vorhanden sind.
        /// </summary>
        public BitmapImage? CoverImage { get; }
    }
}
