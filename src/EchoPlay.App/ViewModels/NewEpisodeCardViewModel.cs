using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        /// Anbieter sinnvoll ist — vor dem Erscheinen steht dort nur eine Vorabveröffentlichung.
        /// </summary>
        private readonly DateTime? _releaseDate;

        /// <summary>
        /// Anbieter-Kennungen der Folge, mitgegeben statt nachgeladen: Eine Neuerscheinung
        /// stammt vom Anbieter und ist noch keine gespeicherte Folge — ihre Id steht in keiner
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
        /// <param name="coverImage">Cover-Bild der Serie – kann null sein.</param>
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
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel. Nullable – Fallback auf <see cref="SystemClock"/>.</param>
        /// <param name="spotifyAlbumId">Spotify-Album-Kennung der Folge, sofern bekannt.</param>
        /// <param name="appleMusicAlbumId">Apple-Music-Album-Kennung der Folge, sofern bekannt.</param>
        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
            Justification = "Lowercase-Badge-Text wird im UI direkt angezeigt und muss kleingeschrieben bleiben.")]
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
            EpisodeTitle = CleanEpisodeTitle(episodeTitle, seriesName);
            _coverImage = coverImage;
            EpisodeNumber = episodeNumber;
            _localizationService = localizationService;
            _releaseDate = releaseDate;

            EpisodeNumberText = episodeNumber.HasValue
                ? string.Format(CultureInfo.CurrentCulture, localizationService?.Get("EpisodeNumberFormat") ?? "Folge {0}", episodeNumber.Value)
                : null;
            IsOnlineOnly = !hasLocalTrack;

            // Info-Zeile für das Kachel-Overlay – einheitliches Format:
            // "Nr. 170 · 03.04.2026 · online" (Neuerscheinung)
            // "Nr. 26 · 25.04.2026 · angekündigt" (Ankündigung)
            if (releaseDate.HasValue)
            {
                List<string> parts = [];

                if (episodeNumber.HasValue)
                {
                    parts.Add($"Nr. {episodeNumber.Value}");
                }

                parts.Add(releaseDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));

                if (isAnnounced)
                {
                    string label = localizationService?.Get("BadgeAnnounced") ?? "Angekündigt";
                    parts.Add(label.ToLowerInvariant());
                }
                else if (!hasLocalTrack)
                {
                    parts.Add("online");
                }

                InfoLineText = string.Join(" · ", parts);
            }
            else
            {
                InfoLineText = null;
            }

            if (releaseDate.HasValue && releaseDate.Value.Date > _clock.UtcNow.Date)
            {
                // Nur das Datum – der Badge zeigt bereits "Angekündigt"
                ReleaseDateText = releaseDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            }
            else
            {
                ReleaseDateText = null;
            }

            // Badge-Logik: Angekündigt > Neu ≤7 Tage > kein Badge. Text und Sichtbarkeit
            // stehen unabhängig von WinUI; nur die Farbe kommt aus der Palette und ist
            // deshalb ohne laufende Anwendung nicht auflösbar.
            if (isAnnounced)
            {
                BadgeText = localizationService?.Get("BadgeAnnounced") ?? "Angekündigt";
                BadgeVisibility = Visibility.Visible;
                BadgeBrush = TryResolveThemeBrush("AccentPrimaryBrush");
            }
            else if (releaseDate.HasValue && (_clock.UtcNow.Date - releaseDate.Value.Date).TotalDays <= 7)
            {
                BadgeText = localizationService?.Get("BadgeNew") ?? "Neu";
                BadgeVisibility = Visibility.Visible;
                BadgeBrush = TryResolveThemeBrush("StatusSuccessBrush");
            }
            else
            {
                BadgeText = null;
                BadgeVisibility = Visibility.Collapsed;
                BadgeBrush = null;
            }

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

        /// <summary>Erscheinungsdatum-Text (z.B. "Erscheint am 15.04.2026") oder null wenn nicht angekündigt.</summary>
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
        /// Cover-Bild – bevorzugt Episoden-Cover, dann Serien-Cover, oder null.
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
        /// Hintergrundfarbe des Badge: Akzentfarbe (blau) für Ankündigungen,
        /// Grün (#4CAF50) für Neuerscheinungen der letzten 7 Tage.
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
            // führt angekündigte Alben zwar schon als Vorabveröffentlichung — zu hören gibt es
            // dort aber nichts, und der Nutzer landet außerhalb der Anwendung.
            bool nochNichtErschienen = _releaseDate.HasValue && _releaseDate.Value.Date > _clock.UtcNow.Date;

            if (nochNichtErschienen || (IsAnnounced && !_releaseDate.HasValue))
            {
                await _errorDialogService.ShowAsync(
                    _localizationService?.Get("EpisodeNotAvailableTitle") ?? "Noch nicht verfügbar",
                    _localizationService?.Get("EpisodeNotAvailableMessage")
                        ?? "Diese Episode ist noch nicht lokal verfügbar und kann noch nicht abgespielt werden.");
                return;
            }

            EpisodeLaunchResult result = await PlaybackLauncher.PlayOrOpenProviderAsync(
                _scopeFactory, _playerService, EpisodeId, SeriesName, EpisodeTitle, _spotifyAlbumId, _appleMusicAlbumId);

            if (result != EpisodeLaunchResult.NothingToPlay)
            {
                return;
            }

            // Der frühere Text sagte bei jeder Folge ohne Datei „noch nicht verfügbar" — das
            // trifft nur den angekündigten Fall oben. Hier gibt es die Folge; sie ist nur
            // nirgends erreichbar.
            await _errorDialogService.ShowAsync(
                _localizationService?.Get("EpisodeNotPlayableTitle") ?? "Nicht abspielbar",
                _localizationService?.Get("EpisodeNotPlayableMessage")
                    ?? "Diese Folge liegt nicht auf diesem Rechner, und es ist kein Anbieter hinterlegt, bei dem sie geöffnet werden könnte.");
        }

        /// <summary>
        /// Markiert die Episode als vollständig gehört und aktualisiert den Wiedergabestatus.
        /// Zeigt vorher einen Bestätigungs-Dialog, da die Aktion den Fortschritt überschreibt.
        /// </summary>
        private async Task MarkAsPlayedAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("EpisodeMarkPlayedTitle"),
                SafeResourceLoader.Get("EpisodeMarkPlayedMessage"));

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
                SafeResourceLoader.Get("EpisodeMarkUnplayedMessage"));

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

        /// <summary>
        /// Löst einen Farbschlüssel der Palette auf.
        /// <para>
        /// Über den Schlüssel und nicht über einen Farbwert im Quelltext: Der Nutzer wählt
        /// zwischen sechs Paletten, und eine hier fest verdrahtete Farbe stünde in fünf davon
        /// daneben. Der Schlüsselsatz ist in allen Paletten vollständig — dass er hier zur
        /// Laufzeit gesucht wird, ändert daran nichts.
        /// </para>
        /// </summary>
        /// <param name="key">Schlüssel aus der Palette, etwa <c>AccentPrimaryBrush</c>.</param>
        /// <returns>Der Pinsel, oder <see langword="null"/> ohne laufende Anwendung (Tests).</returns>
        private static Microsoft.UI.Xaml.Media.Brush? TryResolveThemeBrush(string key)
        {
            try
            {
                return Microsoft.UI.Xaml.Application.Current.Resources[key] as Microsoft.UI.Xaml.Media.Brush;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Ohne WinUI-Runtime gibt es keine Palette — die Kachel bleibt ohne Farbe.
                return null;
            }
        }

        /// <summary>
        /// Entfernt Serienname und Folgennummer-Prefix aus dem iTunes-Albumnamen,
        /// damit nur der reine Episodentitel übrig bleibt (z.B. "Zusammengewachsen"
        /// statt "Kira Kolumna - Folge 26 - Zusammengewachsen").
        /// </summary>
        private static string CleanEpisodeTitle(string title, string seriesName)
        {
            string cleaned = title;

            // Serienname am Anfang entfernen (z.B. "Kira Kolumna - ...")
            if (cleaned.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[seriesName.Length..].TrimStart(' ', '-', '–', '—', ':');
            }

            // Folgennummer-Prefix entfernen:
            // "Folge 26 - Titel", "170 - Titel", "Folge 170: Titel", "26: Titel"
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(cleaned, @"^(?:Folge\s+)?\d+\s*[-–—:]\s*");
            if (match.Success)
            {
                cleaned = cleaned[match.Length..];
            }

            // Falls nichts übrig bleibt, Originaltitel beibehalten
            return string.IsNullOrWhiteSpace(cleaned) ? title : cleaned;
        }
    }
}
