using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine einzelne Episode auf dem Dashboard.
    /// Kapselt Wiedergabestatus, Cover-Bild und die Aktionen „Als gehört" / „Als ungehört" markieren.
    /// </summary>
    public sealed class NewEpisodeCardViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IPlayerService _playerService;
        private readonly ILocalizationService? _localizationService;
        private readonly IClock _clock;

        /// <summary>
        /// Veröffentlichungsdatum der Folge. Entscheidet beim Klick darüber, ob der Sprung zum
        /// Anbieter sinnvoll ist, denn vor dem Erscheinen steht dort nur eine Vorabveröffentlichung.
        /// </summary>
        private readonly DateTime? _releaseDate;

        /// <summary>
        /// Anbieter-Kennungen der Folge, mitgegeben statt nachgeladen: Eine Neuerscheinung
        /// stammt vom Anbieter und ist noch keine gespeicherte Folge; ihre Id steht in keiner
        /// Tabelle, ein Nachladen fände nie etwas.
        /// </summary>
        private readonly string? _spotifyAlbumId;
        private readonly string? _appleMusicAlbumId;

        private PlaybackStatus _status;
        private double _progressPercent;
        private BitmapImage? _coverImage;
        private bool _hasEpisodeCover;

        /// <summary>
        /// Initialisiert das Kachel-ViewModel mit allen Stammdaten und benötigten Services.
        /// </summary>
        /// <param name="episodeId">ID der darzustellenden Episode.</param>
        /// <param name="seriesId">ID der zugehörigen Serie.</param>
        /// <param name="seriesName">Anzeigename der Serie.</param>
        /// <param name="episodeTitle">Titel der Episode.</param>
        /// <param name="coverImage">Cover-Bild der Serie; kann null sein.</param>
        /// <param name="status">Aktueller Wiedergabestatus.</param>
        /// <param name="progressPercent">Fortschritt in Prozent (0–100) für den Fortschrittsbalken.</param>
        /// <param name="hasLocalTrack">Gibt an, ob lokale Audiodateien vorhanden sind.</param>
        /// <param name="isAnnounced">Gibt an, ob die Episode nur angekündigt (noch nicht verfügbar) ist.</param>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe in den Commands.</param>
        /// <param name="errorDialogService">Für Info-Dialoge bei Ankündigungen.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge vor Statusänderungen.</param>
        /// <param name="playerService">Für das Starten der Wiedergabe.</param>
        /// <param name="episodeNumber">Folgennummer für die Anzeige, oder null wenn nicht bekannt.</param>
        /// <param name="releaseDate">Erscheinungsdatum für Ankündigungen, oder null.</param>
        /// <param name="localizationService">Liefert lokalisierte UI-Strings. Nullable für Tests.</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel. Nullable, Fallback auf <see cref="SystemClock"/>.</param>
        /// <param name="spotifyAlbumId">Spotify-Album-Kennung der Folge, sofern bekannt.</param>
        /// <param name="appleMusicAlbumId">Apple-Music-Album-Kennung der Folge, sofern bekannt.</param>
        public NewEpisodeCardViewModel(
            Guid episodeId,
            Guid seriesId,
            string seriesName,
            string episodeTitle,
            BitmapImage? coverImage,
            PlaybackStatus status,
            double progressPercent,
            bool hasLocalTrack,
            bool isAnnounced,
            IServiceScopeFactory scopeFactory,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IPlayerService playerService,
            int? episodeNumber = null,
            DateTime? releaseDate = null,
            ILocalizationService? localizationService = null,
            IClock? clock = null,
            string? spotifyAlbumId = null,
            string? appleMusicAlbumId = null)
        {
            _spotifyAlbumId = spotifyAlbumId;
            _appleMusicAlbumId = appleMusicAlbumId;
            ArgumentNullException.ThrowIfNull(seriesName);
            ArgumentNullException.ThrowIfNull(episodeTitle);
            _clock = clock ?? new SystemClock();
            EpisodeId = episodeId;
            SeriesId = seriesId;
            SeriesName = seriesName;
            EpisodeTitle = EpisodeCardLabels.CleanEpisodeTitle(episodeTitle, seriesName);
            _coverImage = coverImage;
            EpisodeNumber = episodeNumber;
            _localizationService = localizationService;
            _releaseDate = releaseDate;

            EpisodeNumberText = FormatEpisodeNumber(localizationService, episodeNumber);
            IsOnlineOnly = !hasLocalTrack;

            InfoLineText = EpisodeCardLabels.InfoLine(localizationService, episodeNumber, releaseDate, isAnnounced, hasLocalTrack);
            ReleaseDateText = FormatFutureReleaseDate(releaseDate, _clock.UtcNow.Date);

            (BadgeText, BadgeVisibility, BadgeBrush) =
                EpisodeCardLabels.Badge(localizationService, isAnnounced, releaseDate, _clock.UtcNow.Date);

            _status = status;
            _progressPercent = progressPercent;
            HasLocalTrack = hasLocalTrack;
            IsAnnounced = isAnnounced;

            _scopeFactory = scopeFactory;
            _errorDialogService = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _playerService = playerService;

            PlayCommand = new RelayCommand(() => _ = PlayAsync());
            MarkAsPlayedCommand = new RelayCommand(() => _ = MarkAsPlayedAsync());
            MarkAsUnplayedCommand = new RelayCommand(() => _ = MarkAsUnplayedAsync());
        }

        private static string? FormatEpisodeNumber(ILocalizationService? localizationService, int? episodeNumber) =>
            episodeNumber.HasValue
                ? string.Format(CultureInfo.CurrentCulture, localizationService?.Get("EpisodeNumberFormat") ?? "Folge {0}", episodeNumber.Value)
                : null;

        /// <summary>
        /// Das Erscheinungsdatum, aber nur für eine noch nicht erschienene Folge.
        /// </summary>
        private static string? FormatFutureReleaseDate(DateTime? releaseDate, DateTime today) =>
            // Nur das Datum, der Badge zeigt bereits "Angekündigt"
            releaseDate.HasValue && releaseDate.Value.Date > today
                ? releaseDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                : null;

        /// <summary>ID der Episode.</summary>
        public Guid EpisodeId { get; }

        /// <summary>ID der zugehörigen Serie.</summary>
        public Guid SeriesId { get; }

        /// <summary>Anzeigename der Serie.</summary>
        public string SeriesName { get; }

        /// <summary>Episodentitel.</summary>
        public string EpisodeTitle { get; }

        /// <summary>
        /// Automation-Name der Cover-Schaltfläche: nennt die Aktion und die Folge.
        /// </summary>
        public string PlayAutomationName => AutomationNameFormatter.Format(
            _localizationService, "TilePlayAutomationName", "Abspielen: {0}", EpisodeTitle);

        /// <summary>
        /// Automation-Name der Kontextmenü-Schaltfläche.
        /// </summary>
        public string ActionsAutomationName => AutomationNameFormatter.Format(
            _localizationService, "TileActionsAutomationName", "Weitere Aktionen: {0}", EpisodeTitle);

        /// <summary>Episodennummer oder null wenn keine Nummer bekannt ist.</summary>
        public int? EpisodeNumber { get; }

        /// <summary>Formatierte Folgennummer (z.B. "Folge 229") oder null.</summary>
        public string? EpisodeNumberText { get; }

        /// <summary>Erscheinungsdatum einer angekündigten Folge (z.B. "15.04.2026") oder null.</summary>
        public string? ReleaseDateText { get; }

        /// <summary>Sichtbarkeit des Erscheinungsdatums auf der Kachel (nur bei Ankündigungen).</summary>
        public Visibility ReleaseDateVisibility =>
            ReleaseDateText is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Formatierte Info-Zeile für Neuerscheinungs-Kacheln (z.B. "Nr. 234 · 14.02.2026 · online").
        /// Null für Kacheln ohne Erscheinungsdatum oder außerhalb der Neuerscheinungen-Sektion.
        /// </summary>
        public string? InfoLineText { get; }

        /// <summary>
        /// Sichtbarkeit der Info-Zeile im Kachel-Overlay.
        /// </summary>
        public Visibility InfoLineVisibility =>
            InfoLineText is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob die Episode nur online verfügbar ist (nicht lokal vorhanden).
        /// Steuert die Anzeige von "online" in der Info-Zeile und die Akzentfarbe bei Ankündigungen.
        /// </summary>
        public bool IsOnlineOnly { get; }

        /// <summary>
        /// Cover-Bild: bevorzugt Episoden-Cover, dann Serien-Cover, oder null.
        /// Initial wird häufig das Serien-Cover gesetzt, damit die Kachel sofort sichtbar ist;
        /// <see cref="UpdateCoverImage"/> blendet das spezifischere Episoden-Cover nach.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            private set => SetProperty(ref _coverImage, value);
        }

        /// <summary>
        /// Gibt an, ob bereits das spezifische Episoden-Cover (nicht das Serien-Fallback) gesetzt ist.
        /// Wird vom Dashboard-Startpfad genutzt, um fehlende Cover für die Hintergrund-Queue zu sammeln.
        /// </summary>
        public bool HasEpisodeCover => _hasEpisodeCover;

        /// <summary>
        /// Tauscht das Cover-Bild gegen das echte Episoden-Cover aus.
        /// Muss auf dem UI-Thread aufgerufen werden, da <see cref="BitmapImage"/> nur dort lebt.
        /// </summary>
        public void UpdateCoverImage(BitmapImage? bitmap)
        {
            if (bitmap is null)
            {
                return;
            }

            _hasEpisodeCover = true;
            CoverImage = bitmap;
        }

        /// <summary>
        /// Setzt Episoden-Cover und Episoden-Cover-Flag zurück, damit Dashboard-Refreshs
        /// die ausgetauschten Karten nicht bis zum nächsten GC-Lauf am Heap halten.
        /// </summary>
        public void ClearCoverImage()
        {
            _hasEpisodeCover = false;
            CoverImage = null;
        }

        /// <summary>
        /// Aktueller Wiedergabestatus der Episode.
        /// Wird nach „Als gehört" / „Als ungehört" markieren aktualisiert.
        /// </summary>
        public PlaybackStatus Status
        {
            get => _status;
            private set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(ProgressBarVisibility));
                    OnPropertyChanged(nameof(CompletedCheckVisibility));
                }
            }
        }

        /// <summary>
        /// Steuert die Sichtbarkeit des Fortschrittsbalkens.
        /// Der Balken erscheint nur, wenn die Episode angefangen aber noch nicht abgeschlossen ist.
        /// </summary>
        public Visibility ProgressBarVisibility =>
            _status == PlaybackStatus.InProgress ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des grünen Hakens auf dem Cover.
        /// Wird eingeblendet wenn die Episode vollständig gehört wurde.
        /// </summary>
        public Visibility CompletedCheckVisibility =>
            _status == PlaybackStatus.Finished ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Fortschritt in Prozent (0–100) für den Fortschrittsbalken.</summary>
        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        /// <summary>Gibt an, ob lokale Audiodateien für diese Episode vorhanden sind.</summary>
        public bool HasLocalTrack { get; }

        /// <summary>
        /// Gibt an, ob diese Episode nur angekündigt ist (kein lokaler Track, Veröffentlichung in der Zukunft oder unbekannt).
        /// Ankündigungs-Episoden können nicht abgespielt werden.
        /// </summary>
        public bool IsAnnounced { get; }

        /// <summary>
        /// Badge-Text für die Kachel: "Angekündigt" (blau), "Neu" (grün, ≤ 7 Tage), oder null (kein Badge).
        /// </summary>
        public string? BadgeText { get; }

        /// <summary>
        /// Sichtbarkeit des Badge-Elements auf der Kachel.
        /// Visible wenn <see cref="BadgeText"/> gesetzt ist, sonst Collapsed.
        /// </summary>
        public Visibility BadgeVisibility { get; }

        /// <summary>
        /// Hintergrundfarbe des Badge aus der Palette: die Akzentfarbe für Ankündigungen,
        /// die Erfolgsfarbe für Neuerscheinungen der letzten 7 Tage.
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush? BadgeBrush { get; }

        /// <summary>Wird bei Klick auf die Kachel ausgelöst. Startet Wiedergabe oder zeigt Ankündigungs-Info.</summary>
        public ICommand PlayCommand { get; }

        /// <summary>Markiert die Episode über das Kachel-Menü als vollständig gehört.</summary>
        public ICommand MarkAsPlayedCommand { get; }

        /// <summary>Setzt den Wiedergabestatus der Episode über das Kachel-Menü zurück.</summary>
        public ICommand MarkAsUnplayedCommand { get; }

        /// <summary>
        /// Startet die Wiedergabe der Episode, wenn lokale Tracks vorhanden sind.
        /// Bei Ankündigungen wird stattdessen ein Hinweis-Dialog angezeigt.
        /// </summary>
        private async Task PlayAsync()
        {
            // Maßgeblich ist das Veröffentlichungsdatum, nicht das Kennzeichen: Erst wenn das
            // Album vollständig zur Verfügung steht, lohnt der Sprung zum Anbieter. Apple Music
            // führt angekündigte Alben zwar schon als Vorabveröffentlichung, zu hören gibt es
            // dort aber nichts, und der Nutzer landet außerhalb der Anwendung.
            bool notYetReleased = _releaseDate.HasValue && _releaseDate.Value.Date > _clock.UtcNow.Date;

            if (notYetReleased || (IsAnnounced && !_releaseDate.HasValue))
            {
                await _errorDialogService.ShowAsync(
                    _localizationService?.Get("EpisodeNotAvailableTitle") ?? "Noch nicht verfügbar",
                    _localizationService?.Get("EpisodeNotAvailableMessage")
                        ?? "Diese Episode ist noch nicht lokal verfügbar und kann noch nicht abgespielt werden.",
                    DialogKey.EpisodeNotAvailable);
                return;
            }

            EpisodeLaunchResult result = await PlaybackLauncher.PlayOrOpenProviderAsync(
                _scopeFactory, _playerService, EpisodeId, SeriesName, EpisodeTitle, _spotifyAlbumId, _appleMusicAlbumId);

            if (result != EpisodeLaunchResult.NothingToPlay)
            {
                return;
            }

            // „Noch nicht verfügbar" trifft nur den angekündigten Fall oben. Hier gibt es die Folge; sie ist nur
            // nirgends erreichbar.
            await _errorDialogService.ShowAsync(
                _localizationService?.Get("EpisodeNotPlayableTitle") ?? "Nicht abspielbar",
                _localizationService?.Get("EpisodeNotPlayableMessage")
                    ?? "Diese Folge liegt nicht auf diesem Rechner, und es ist kein Anbieter hinterlegt, bei dem sie geöffnet werden könnte.",
                DialogKey.EpisodeNotPlayable);
        }

        /// <summary>
        /// Markiert die Episode als vollständig gehört und aktualisiert den Wiedergabestatus.
        /// Zeigt vorher einen Bestätigungs-Dialog, da die Aktion den Fortschritt überschreibt.
        /// </summary>
        private async Task MarkAsPlayedAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("EpisodeMarkPlayedTitle"),
                SafeResourceLoader.Get("EpisodeMarkPlayedMessage"),
                DialogKey.EpisodeMarkPlayed);

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            await stateService.MarkCompletedAsync(EpisodeId, _clock.UtcNow);

            Status = PlaybackStatus.Finished;
            ProgressPercent = 100;
        }

        /// <summary>
        /// Setzt den Wiedergabestatus der Episode zurück, indem der PlaybackState-Eintrag gelöscht wird.
        /// Zeigt vorher einen Bestätigungs-Dialog, da der Fortschritt verloren geht.
        /// </summary>
        private async Task MarkAsUnplayedAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("EpisodeMarkUnplayedTitle"),
                SafeResourceLoader.Get("EpisodeMarkUnplayedMessage"),
                DialogKey.EpisodeMarkUnplayed);

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            await stateService.MarkNotStartedAsync(EpisodeId);

            Status = PlaybackStatus.NotStarted;
            ProgressPercent = 0;
        }
    }
}
