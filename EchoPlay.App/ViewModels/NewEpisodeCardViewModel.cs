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

        private PlaybackStatus _status;
        private double _progressPercent;

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
            IClock? clock = null)
        {
            ArgumentNullException.ThrowIfNull(seriesName);
            ArgumentNullException.ThrowIfNull(episodeTitle);
            _clock       = clock ?? new SystemClock();
            EpisodeId    = episodeId;
            SeriesId     = seriesId;
            SeriesName   = seriesName;
            EpisodeTitle = CleanEpisodeTitle(episodeTitle, seriesName);
            CoverImage   = coverImage;
            EpisodeNumber = episodeNumber;
            _localizationService = localizationService;

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

            // Akzentfarbe für Ankündigungen, halbtransparentes Weiß für reguläre Neuerscheinungen.
            // try/catch: WinUI-Ressourcen sind in Unit-Tests ohne App-Instanz nicht verfügbar.
            try
            {
                InfoLineForeground = isAnnounced
                    ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemAccentColorLight2Brush"]
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.73 };
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // In Unit-Tests ohne WinUI-Runtime: Fallback auf null
                InfoLineForeground = null!;
            }

            // Badge-Logik: Angekündigt (blau) > Neu ≤7 Tage (grün) > kein Badge.
            // Text und Visibility werden unabhängig von WinUI gesetzt.
            // Nur der Brush braucht den try/catch (WinUI-Ressourcen in Unit-Tests nicht verfügbar).
            if (isAnnounced)
            {
                BadgeText = localizationService?.Get("BadgeAnnounced") ?? "Angekündigt";
                BadgeVisibility = Visibility.Visible;
                BadgeBrush = TryResolveAccentBrush();
            }
            else if (releaseDate.HasValue && (_clock.UtcNow.Date - releaseDate.Value.Date).TotalDays <= 7)
            {
                BadgeText = localizationService?.Get("BadgeNew") ?? "Neu";
                BadgeVisibility = Visibility.Visible;
                BadgeBrush = TryCreateBrush(255, 76, 175, 80);
            }
            else
            {
                BadgeText = null;
                BadgeVisibility = Visibility.Collapsed;
                BadgeBrush = null;
            }

            _status      = status;
            _progressPercent = progressPercent;
            HasLocalTrack    = hasLocalTrack;
            IsAnnounced      = isAnnounced;

            _scopeFactory               = scopeFactory;
            _errorDialogService         = errorDialogService;
            _confirmationDialogService  = confirmationDialogService;
            _playerService              = playerService;

            PlayCommand           = new RelayCommand(() => _ = PlayAsync());
            MarkAsPlayedCommand   = new RelayCommand(() => _ = MarkAsPlayedAsync());
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
        /// Vordergrundfarbe der Info-Zeile auf der Kachel.
        /// Ankündigungen (Datum in der Zukunft) werden in der Akzentfarbe angezeigt,
        /// reguläre Neuerscheinungen in halbtransparentem Weiß.
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush InfoLineForeground { get; }


        /// <summary>Cover-Bild – bevorzugt Episoden-Cover, dann Serien-Cover, oder null.</summary>
        public BitmapImage? CoverImage { get; }

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
            if (IsAnnounced || !HasLocalTrack)
            {
                string notAvailableTitle = _localizationService?.Get("EpisodeNotAvailableTitle") ?? "Noch nicht verfügbar";
                string notAvailableMessage = _localizationService?.Get("EpisodeNotAvailableMessage")
                    ?? "Diese Episode ist noch nicht lokal verfügbar und kann noch nicht abgespielt werden.";

                await _errorDialogService.ShowAsync(notAvailableTitle, notAvailableMessage);
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService    = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(EpisodeId);

            if (tracks.Count == 0)
            {
                return;
            }

            PlaybackState? savedState = await stateService.GetByEpisodeIdAsync(EpisodeId);
            // Nur fortsetzen, wenn die Episode nicht bereits abgeschlossen ist
            TimeSpan resumePosition = savedState is { IsCompleted: false } ? savedState.LastPosition : TimeSpan.Zero;

            List<string> paths = new(tracks.Count);

            foreach (LocalTrack track in tracks)
            {
                paths.Add(track.FilePath);
            }

            _playerService.Play(EpisodeId, paths, startIndex: 0, resumePosition: resumePosition);
        }

        /// <summary>
        /// Markiert die Episode als vollständig gehört und aktualisiert den Wiedergabestatus.
        /// Zeigt vorher einen Bestätigungs-Dialog, da die Aktion den Fortschritt überschreibt.
        /// </summary>
        private async Task MarkAsPlayedAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Als gehört markieren?",
                "Diese Episode wird als vollständig gehört gespeichert.");

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            PlaybackState? existing = await stateService.GetByEpisodeIdAsync(EpisodeId);

            if (existing is not null)
            {
                existing.IsCompleted = true;
                existing.CompletedAt = _clock.UtcNow;
                await stateService.UpdateAsync(existing);
            }
            else
            {
                PlaybackState newState = new()
                {
                    EpisodeId   = EpisodeId,
                    IsCompleted = true,
                    CompletedAt = _clock.UtcNow,
                    LastPosition = TimeSpan.Zero,
                    LastPlayedAt = _clock.UtcNow
                };
                await stateService.AddAsync(newState);
            }

            Status          = PlaybackStatus.Finished;
            ProgressPercent = 100;
        }

        /// <summary>
        /// Setzt den Wiedergabestatus der Episode zurück, indem der PlaybackState-Eintrag gelöscht wird.
        /// Zeigt vorher einen Bestätigungs-Dialog, da der Fortschritt verloren geht.
        /// </summary>
        private async Task MarkAsUnplayedAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Als ungehört markieren?",
                "Der Fortschritt dieser Episode wird gelöscht.");

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            PlaybackState? existing = await stateService.GetByEpisodeIdAsync(EpisodeId);

            if (existing is not null)
            {
                await stateService.DeleteAsync(existing.Id);
            }

            Status          = PlaybackStatus.NotStarted;
            ProgressPercent = 0;
        }

        /// <summary>
        /// Versucht die WinUI-Akzentfarbe als Brush aufzulösen.
        /// Gibt null zurück wenn keine WinUI-Runtime verfügbar ist (Unit-Tests).
        /// </summary>
        private static Microsoft.UI.Xaml.Media.Brush TryResolveAccentBrush()
        {
            try
            {
                return (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current
                    .Resources["SystemAccentColorLight2Brush"];
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null!;
            }
        }

        /// <summary>
        /// Erstellt einen SolidColorBrush aus ARGB-Werten.
        /// Gibt null zurück wenn keine WinUI-Runtime verfügbar ist (Unit-Tests).
        /// </summary>
        private static Microsoft.UI.Xaml.Media.SolidColorBrush? TryCreateBrush(byte a, byte r, byte g, byte b)
        {
            try
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null!;
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
