using Microsoft.UI.Xaml;
using System;

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
        /// <param name="seriesId">Kennung der Serie; ein Klick öffnet ihre Detailseite.</param>
        /// <param name="seriesName">Name der Serie.</param>
        /// <param name="episodeTitle">Albumname beim Anbieter.</param>
        /// <param name="dateLine">Erscheinungstag und Folgennummer, fertig formatiert.</param>
        /// <param name="coverUri">Adresse des Covers, oder <c>null</c>.</param>
        /// <param name="badgeText">Kennzeichen oben links, oder <c>null</c> für keines.</param>
        /// <param name="releaseDateUtc">Erscheinungszeitpunkt (UTC).</param>
        /// <param name="isAnnounced">Ob die Folge noch nicht erschienen ist.</param>
        /// <param name="availability">Wo die Folge im Bestand liegt.</param>
        public ReleaseCalendarItemViewModel(
            Guid seriesId,
            string seriesName,
            string episodeTitle,
            string dateLine,
            Uri? coverUri,
            string? badgeText,
            DateTime releaseDateUtc,
            bool isAnnounced,
            ReleaseAvailability availability)
        {
            SeriesId = seriesId;
            SeriesName = seriesName;
            EpisodeTitle = episodeTitle;
            DateLine = dateLine;
            CoverUri = coverUri;
            BadgeText = badgeText;
            ReleaseDateUtc = releaseDateUtc;
            IsAnnounced = isAnnounced;
            Availability = availability;
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
