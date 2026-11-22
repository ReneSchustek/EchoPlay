using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Wo eine Veröffentlichung im eigenen Bestand schon liegt.
    /// </summary>
    public enum ReleaseAvailability
    {
        /// <summary>Weder lokal noch in der Online-Mediathek.</summary>
        Missing,

        /// <summary>Als Folge in der Online-Mediathek, aber ohne Datei.</summary>
        InLibrary,

        /// <summary>Mit mindestens einer Datei lokal vorhanden.</summary>
        Local
    }

    /// <summary>
    /// Eine Kachel im Veröffentlichungskalender der Startseite.
    /// </summary>
    /// <remarks>
    /// Das Cover kommt als Adresse, nicht als fertiges Bild: Die Ansicht dekodiert es in
    /// Kachelgröße, und das Ansichtsmodell bleibt ohne WinUI-Bildtypen im Testprozess baubar.
    /// </remarks>
    public sealed class ReleaseCalendarItemViewModel
    {
        /// <summary>
        /// Initialisiert die Kachel.
        /// </summary>
        /// <param name="data">Was der Eintrag anzeigt.</param>
        public ReleaseCalendarItemViewModel(ReleaseCalendarItemData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            SeriesId = data.SeriesId;
            SeriesName = data.SeriesName;
            EpisodeTitle = data.EpisodeTitle;
            DateLine = data.DateLine;
            CoverUri = data.CoverUri;
            BadgeText = data.BadgeText;
            ReleaseDateUtc = data.ReleaseDateUtc;
            IsAnnounced = data.IsAnnounced;
            Availability = data.Availability;
        }

        /// <summary>Kennung der Serie.</summary>
        public Guid SeriesId { get; }

        /// <summary>Name der Serie.</summary>
        public string SeriesName { get; }

        /// <summary>Albumname beim Anbieter.</summary>
        public string EpisodeTitle { get; }

        /// <summary>Erscheinungstag und Folgennummer, etwa „14. November · Nr. 238".</summary>
        public string DateLine { get; }

        /// <summary>Adresse des Covers, oder <c>null</c>.</summary>
        public Uri? CoverUri { get; }

        /// <summary>Kennzeichen oben links, etwa „Angekündigt" oder „Vorhanden".</summary>
        public string? BadgeText { get; }

        /// <summary>Sichtbarkeit des Kennzeichens.</summary>
        public Visibility BadgeVisibility => BadgeText is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Erscheinungszeitpunkt (UTC).</summary>
        public DateTime ReleaseDateUtc { get; }

        /// <summary>Ob die Folge noch nicht erschienen ist.</summary>
        public bool IsAnnounced { get; }

        /// <summary>Wo die Folge im Bestand liegt.</summary>
        public ReleaseAvailability Availability { get; }

        /// <summary>Name für Bildschirmleser: Serie, Titel und Tag in einem Satz.</summary>
        public string AutomationName => $"{SeriesName}, {EpisodeTitle}, {DateLine}";
    }
}
