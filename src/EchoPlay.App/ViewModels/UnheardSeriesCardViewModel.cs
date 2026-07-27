using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für den „Weiterhören"-Abschnitt der Startseite.
    /// Zeigt Serien an, bei denen der Nutzer mindestens eine Folge gehört hat,
    /// aber noch nicht alle – ein Hinweis zum Weiterhören.
    /// </summary>
    public sealed class UnheardSeriesCardViewModel
    {
        private readonly EchoPlay.App.Services.ILocalizationService? _localizationService;

        /// <summary>
        /// Erstellt eine Kachel für eine angefangene Serie mit ungehörten Folgen.
        /// </summary>
        /// <param name="seriesId">Datenbank-ID der Serie (für Navigation zur Detailseite).</param>
        /// <param name="seriesName">Titel der Serie.</param>
        /// <param name="coverImage">Serien-Cover oder null.</param>
        /// <param name="unheardCount">Anzahl der noch nicht gehörten Folgen.</param>
        /// <param name="localizationService">Für den Automation-Namen der Kachel. Nullable für Tests.</param>
        public UnheardSeriesCardViewModel(
            Guid seriesId,
            string seriesName,
            BitmapImage? coverImage,
            int unheardCount,
            EchoPlay.App.Services.ILocalizationService? localizationService = null)
        {
            _localizationService = localizationService;
            SeriesId = seriesId;
            SeriesName = seriesName;
            CoverImage = coverImage;
            UnheardCount = unheardCount;

            // Singular und Plural sind eigene Ressourcen – im Englischen unterscheiden
            // sich die Formen anders als im Deutschen, ein Suffix-Anhängen genügt nicht.
            DisplayText = unheardCount == 1
                ? localizationService?.Get("UnheardEpisodesSingular") ?? "1 ungehörte Folge"
                : string.Format(
                    CultureInfo.CurrentCulture,
                    localizationService?.Get("UnheardEpisodesPlural") ?? "{0} ungehörte Folgen",
                    unheardCount);
        }

        /// <summary>Datenbank-ID der Serie – für Navigation zur Detailseite.</summary>
        public Guid SeriesId { get; }

        /// <summary>Titel der Serie.</summary>
        public string SeriesName { get; }

        /// <summary>
        /// Automation-Name der Kachel-Schaltfläche: nennt die Aktion und die Serie.
        /// </summary>
        public string OpenAutomationName => AutomationNameFormatter.Format(
            _localizationService, "TileOpenAutomationName", "Öffnen: {0}", SeriesName);

        /// <summary>Serien-Cover oder null wenn keines vorhanden.</summary>
        public BitmapImage? CoverImage { get; }

        /// <summary>Anzahl der noch nicht gehörten Folgen.</summary>
        public int UnheardCount { get; }

        /// <summary>Anzeigetext, z.B. "12 ungehörte Folgen".</summary>
        public string DisplayText { get; }
    }
}
