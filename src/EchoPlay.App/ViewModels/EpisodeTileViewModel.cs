using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Globalization;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Episode in der Folgen-Ansicht der <see cref="EchoPlay.App.Views.SeriesDetailPage"/>.
    /// Entspricht dem Konzept von <see cref="LocalEpisodeCardViewModel"/>, erweitert um
    /// Wiedergabeinformationen und ein optionales Episodenbild.
    /// Das ViewModel ist unveränderlich – bei Datenänderungen wird es neu erzeugt.
    /// </summary>
    public sealed class EpisodeTileViewModel
    {
        /// <summary>
        /// Erstellt ein Kachel-ViewModel für eine Episode.
        /// </summary>
        /// <param name="episodeId">Datenbank-ID der Episode.</param>
        /// <param name="episodeNumber">Episodennummer oder null wenn nicht vorhanden.</param>
        /// <param name="title">Episodentitel.</param>
        /// <param name="totalDuration">Gesamtdauer der Episode oder null wenn unbekannt.</param>
        /// <param name="playbackStatus">Wiedergabestatus der Episode.</param>
        /// <param name="releaseDate">Erscheinungsdatum oder null.</param>
        /// <param name="playEpisode">
        /// Callback zum Abspielen dieser Episode.
        /// Wird als RelayCommand verpackt, damit die Page/ViewModel-Grenze gewahrt bleibt.
        /// </param>
        /// <param name="progressPercent">Wiedergabefortschritt in Prozent (0–100).</param>
        /// <param name="isSpecialEpisode">Ob es sich um eine Sonderfolge handelt.</param>
        /// <param name="coverImage">Vorab geladenes Cover oder null für Platzhalter.</param>
        public EpisodeTileViewModel(
            Guid episodeId,
            int? episodeNumber,
            string title,
            TimeSpan? totalDuration,
            PlaybackStatus playbackStatus,
            DateTime? releaseDate,
            Action playEpisode,
            double progressPercent = 0,
            bool isSpecialEpisode = false,
            BitmapImage? coverImage = null)
        {
            EpisodeId = episodeId;
            EpisodeNumber = episodeNumber;
            Title = title;
            TotalDuration = totalDuration;
            Progress = playbackStatus;
            ReleaseDate = releaseDate;
            ProgressPercent = progressPercent;
            IsSpecialEpisode = isSpecialEpisode;
            CoverImage = coverImage;
            PlayCommand = new RelayCommand(playEpisode);
        }

        /// <summary>Datenbank-ID der Episode.</summary>
        public Guid EpisodeId { get; }

        /// <summary>
        /// Sonderfolge: Nummer 0, 000-Präfix oder ohne Nummer.
        /// Wird in einem eigenen Tab dargestellt.
        /// </summary>
        public bool IsSpecialEpisode { get; }

        /// <summary>
        /// Cover-Bild der Episode. Null wenn kein Cover vorhanden –
        /// die UI zeigt dann ein Platzhalter-Icon.
        /// </summary>
        public BitmapImage? CoverImage { get; }

        /// <summary>
        /// Sichtbarkeit des Platzhalter-Icons: eingeblendet wenn kein Cover vorhanden.
        /// </summary>
        public Visibility NoCoverVisibility => CoverImage is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Episodennummer oder null wenn keine Nummer bekannt ist.</summary>
        public int? EpisodeNumber { get; }

        /// <summary>Episodentitel.</summary>
        public string Title { get; }

        /// <summary>Gesamtdauer der Episode oder null wenn keine Dauer bekannt ist.</summary>
        public TimeSpan? TotalDuration { get; }

        /// <summary>Erscheinungsdatum der Episode oder null.</summary>
        public DateTime? ReleaseDate { get; }

        /// <summary>Wiedergabefortschritt der Episode (NotStarted / InProgress / Finished).</summary>
        public PlaybackStatus Progress { get; }

        /// <summary>
        /// Kombinierter Anzeige-Titel aus Episodennummer und Bezeichnung, z.B. "001 – Titel".
        /// Ohne Nummer wird nur der Titel angezeigt.
        /// </summary>
        public string DisplayTitle => EpisodeNumber.HasValue
            ? $"{EpisodeNumber.Value:D3} \u2013 {Title}"
            : Title;

        /// <summary>
        /// Formatierte Dauer, z.B. "1:23:45".
        /// Leer wenn keine Dauer bekannt ist.
        /// </summary>
        public string DurationText => TotalDuration.HasValue && TotalDuration.Value > TimeSpan.Zero
            ? TotalDuration.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : string.Empty;

        /// <summary>
        /// Segoe-MDL2-Assets-Glyph passend zum Wiedergabefortschritt.
        /// Wird in der Kachelansicht als kleines Symbol neben der Folge angezeigt.
        /// </summary>
        public string StatusGlyph => Progress switch
        {
            // Häkchen (E8FB) für abgeschlossen
            PlaybackStatus.Finished => "\uE8FB",
            // Fortschritt (E916) für teilweise gehört
            PlaybackStatus.InProgress => "\uE916",
            // Leerer Kreis (E73E) für noch nicht gespielt
            _ => "\uE73E"
        };

        /// <summary>
        /// Startet die Wiedergabe aller Tracks dieser Folge.
        /// Wird von <see cref="SeriesDetailViewModel"/> mit der passenden Aktion belegt.
        /// </summary>
        public ICommand PlayCommand { get; }

        /// <summary>Wiedergabefortschritt in Prozent (0–100) für den Fortschrittsbalken.</summary>
        public double ProgressPercent { get; }

        /// <summary>
        /// Sichtbarkeit des Fortschrittsbalkens: nur bei angefangenen Episoden.
        /// </summary>
        public Visibility ProgressBarVisibility =>
            Progress == PlaybackStatus.InProgress ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des grünen Hakens: nur bei gehörten Episoden.
        /// </summary>
        public Visibility CompletedCheckVisibility =>
            Progress == PlaybackStatus.Finished ? Visibility.Visible : Visibility.Collapsed;
    }
}
