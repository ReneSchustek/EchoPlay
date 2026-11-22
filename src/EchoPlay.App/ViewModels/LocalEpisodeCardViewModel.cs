using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Episode in der mittleren Spalte der lokalen Mediathek.
    /// Nur Episoden mit einem zugeordneten lokalen Ordner werden hier angezeigt.
    /// </summary>
    /// <remarks>
    /// Erweitert <see cref="CoverCardViewModelBase"/>, damit <see cref="CoverCardViewModelBase.CoverImage"/> nachträglich
    /// gesetzt werden kann, zum Beispiel wenn der Nutzer über das Kontextmenü ein neues
    /// Cover auswählt oder eine Cover-Suche ausführt.
    /// </remarks>
    public sealed class LocalEpisodeCardViewModel : CoverCardViewModelBase
    {
        private bool _isCompleted;

        /// <summary>
        /// Erstellt ein Kachel-ViewModel für die mittlere Spalte.
        /// </summary>
        /// <param name="data">Was die Kachel anzeigt.</param>
        /// <param name="coverImage">
        /// Vorab geladenes Cover-Bild oder <see langword="null"/> wenn kein Cover vorhanden.
        /// </param>
        public LocalEpisodeCardViewModel(
            LocalEpisodeCardData data,
            BitmapImage? coverImage = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            EpisodeId = data.EpisodeId;
            EpisodeNumber = data.EpisodeNumber;
            Title = data.Title;
            LocalTrackCount = data.LocalTrackCount;
            FolderPath = data.FolderPath;
            IsSpecialEpisode = data.IsSpecialEpisode;
            CoverImage = coverImage;
            _isCompleted = data.IsCompleted;
        }

        /// <summary>Datenbank-ID der Episode.</summary>
        public Guid EpisodeId { get; }

        /// <summary>Episodennummer oder null wenn die Folge keine Nummer hat.</summary>
        public int? EpisodeNumber { get; }

        /// <summary>Episodentitel.</summary>
        public string Title { get; }

        /// <summary>
        /// Gibt an, ob es sich um eine Sonderfolge handelt (Nummer 0, 000-Präfix oder ohne Nummer).
        /// Sonderfolgen werden in einem eigenen Tab angezeigt.
        /// </summary>
        public bool IsSpecialEpisode { get; }

        /// <summary>Anzahl der lokal zugeordneten Audiodateien dieser Episode.</summary>
        public int LocalTrackCount { get; }

        /// <summary>
        /// Pfad zum Episodenordner.
        /// Wird beim "Alle Tracks dieser Folge bearbeiten"-Button als Argument übergeben.
        /// </summary>
        public string? FolderPath { get; }

        /// <summary>
        /// Kombinierter Anzeige-Titel aus Episodennummer und Bezeichnung.
        /// Beispiel: "001 – TKKG und der Unsichtbare". Ohne Nummer wird nur der Titel angezeigt.
        /// </summary>
        public string DisplayTitle => EpisodeNumber.HasValue
            ? $"{EpisodeNumber.Value:D3} \u2013 {Title}"
            : Title;

        /// <summary>
        /// Anzeige-Text der Track-Anzahl, z.B. "3 Tracks".
        /// </summary>
        public string TrackCountText => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            EchoPlay.App.Helpers.PluralText.Pattern(
                LocalTrackCount, "LocalTrackCountSingular", "LocalTrackCountPlural", "{0} Track", "{0} Tracks"),
            LocalTrackCount);

        /// <summary>
        /// Formatierte Episodennummer für die Kachelansicht, z.B. "001".
        /// Gibt "—" zurück wenn keine Nummer vorhanden ist.
        /// </summary>
        public string EpisodeNumberText => EpisodeNumber.HasValue ? $"{EpisodeNumber:000}" : "\u2014";

        /// <summary>
        /// Gibt an, ob die Episode als gehört markiert ist.
        /// Wird nach "Als gehört markieren" über das ViewModel aktualisiert.
        /// </summary>
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (SetProperty(ref _isCompleted, value))
                {
                    OnPropertyChanged(nameof(CompletedCheckVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des grünen Hakens neben der Folgennummer.
        /// Wird eingeblendet wenn die Episode als gehört markiert ist.
        /// </summary>
        public Visibility CompletedCheckVisibility =>
            _isCompleted ? Visibility.Visible : Visibility.Collapsed;
    }
}
