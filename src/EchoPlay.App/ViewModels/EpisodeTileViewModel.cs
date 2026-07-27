using EchoPlay.App.Helpers;
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
        private readonly EchoPlay.App.Services.ILocalizationService? _localizationService;

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
        /// <param name="localizationService">Für den Automation-Namen der Kontextmenü-Schaltfläche. Nullable für Tests.</param>
        /// <param name="spotifyAlbumId">Spotify-Album-ID der Folge, sofern bekannt.</param>
        /// <param name="hasLocalTrack">Ob mindestens eine lokale Audiodatei vorliegt.</param>
        /// <param name="appleMusicAlbumId">Apple-Music-Album-ID der Folge, sofern bekannt.</param>
        /// <param name="seriesTitle">Serientitel – Suchbegriff für Spotify, wenn keine Album-ID vorliegt.</param>
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
            BitmapImage? coverImage = null,
            EchoPlay.App.Services.ILocalizationService? localizationService = null,
            string? spotifyAlbumId = null,
            bool hasLocalTrack = true,
            string? appleMusicAlbumId = null,
            string? seriesTitle = null)
        {
            SpotifyAlbumId = spotifyAlbumId;
            AppleMusicAlbumId = appleMusicAlbumId;
            SeriesTitle = seriesTitle;
            HasLocalTrack = hasLocalTrack;
            _localizationService = localizationService;
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
        /// Automation-Name der Kontextmen\u00fc-Schaltfl\u00e4che auf der Folgen-Kachel.
        /// </summary>
        public string ActionsAutomationName => AutomationNameFormatter.Format(
            _localizationService, "TileActionsAutomationName", "Weitere Aktionen: {0}", DisplayTitle);

        /// <summary>Spotify-Album-ID der Folge, sofern bekannt.</summary>
        public string? SpotifyAlbumId { get; }

        /// <summary>Ob mindestens eine lokale Audiodatei vorliegt.</summary>
        public bool HasLocalTrack { get; }

        /// <summary>Serientitel \u2013 wird f\u00fcr die Spotify-Suche gebraucht, wenn keine Album-ID vorliegt.</summary>
        public string? SeriesTitle { get; }

        /// <summary>
        /// Sichtbarkeit der Aktion \u201eIn Spotify \u00f6ffnen".
        /// Nur f\u00fcr Folgen ohne lokale Datei \u2014 wo lokal abgespielt werden kann, ist der Umweg
        /// \u00fcber Spotify kein Gewinn. Ohne Album-ID greift die Suche, damit Spotify unabh\u00e4ngig
        /// davon nutzbar bleibt, \u00fcber welchen Anbieter importiert wurde.
        /// </summary>
        public Visibility OpenInSpotifyVisibility =>
            !HasLocalTrack && TryBuildSpotifyUrl(out _)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// \u00d6ffnet die Folge in Spotify (App oder Webseite): mit bekannter Album-ID direkt das
        /// Album, sonst die Spotify-Suche nach Serie und Folge.
        /// Startet dort nichts \u2014 die Wiedergabe l\u00f6st der Nutzer selbst aus.
        /// </summary>
        /// <returns><see langword="true"/>, wenn der Link ge\u00f6ffnet werden konnte.</returns>
        public bool OpenInSpotify()
        {
            // Erst die App: dort ist der Nutzer angemeldet, im Browser meist nicht.
            // Ist Spotify nicht installiert, meldet der Launcher das und der Web-Link greift.
            if (SpotifyAlbumLink.TryBuildAppUri(SpotifyAlbumId, out string? appUri)
                || SpotifyAlbumLink.TrySearchAppUri([SeriesTitle, Title], out appUri))
            {
                if (SafeUrlLauncher.TryOpenAppLink(appUri, "spotify"))
                {
                    return true;
                }
            }

            return TryBuildSpotifyUrl(out string? url) && SafeUrlLauncher.TryOpenInBrowser(url);
        }

        /// <summary>
        /// Album-Link bevorzugt, Suchlink als Auffangl\u00f6sung.
        /// </summary>
        private bool TryBuildSpotifyUrl(out string? url)
        {
            if (SpotifyAlbumLink.TryBuild(SpotifyAlbumId, out url))
            {
                return true;
            }

            return SpotifyAlbumLink.TrySearch([SeriesTitle, Title], out url);
        }

        /// <summary>Apple-Music-Album-ID der Folge, sofern bekannt.</summary>
        public string? AppleMusicAlbumId { get; }

        /// <summary>
        /// Sichtbarkeit der Aktion „In Apple Music öffnen" – gleiche Regel wie bei Spotify:
        /// nur für Folgen ohne lokale Datei und mit bekannter Album-ID.
        /// </summary>
        public Visibility OpenInAppleMusicVisibility =>
            !HasLocalTrack && AppleMusicAlbumLink.TryBuild(AppleMusicAlbumId, out _)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// Öffnet das zugehörige Album in Apple Music (App oder Webseite).
        /// Startet dort nichts — die Wiedergabe löst der Nutzer selbst aus.
        /// </summary>
        /// <returns><see langword="true"/>, wenn der Link geöffnet werden konnte.</returns>
        public bool OpenInAppleMusic()
        {
            return AppleMusicAlbumLink.TryBuild(AppleMusicAlbumId, out string? url)
                   && SafeUrlLauncher.TryOpenInBrowser(url);
        }

        /// <summary>
        /// Sichtbarkeit des Trenners vor den Anbieter-Aktionen: nur wenn mindestens eine davon
        /// sichtbar ist, sonst steht ein Strich ohne Inhalt darunter.
        /// </summary>
        public Visibility ProviderActionsSeparatorVisibility =>
            OpenInSpotifyVisibility == Visibility.Visible || OpenInAppleMusicVisibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;

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
