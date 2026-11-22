using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Episode in der Online-Mediathek.
    /// Zeigt Titel, Nummer, Erscheinungsdatum und Cover.
    /// Online-Episoden haben keine lokalen Tracks, stattdessen gibt es
    /// einen „Im Browser öffnen"-Button für den Direktzugriff beim Provider.
    /// </summary>
    public sealed class OnlineEpisodeCardViewModel : CoverCardViewModelBase
    {
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly IClock _clock;
        private bool _isCompleted;

        /// <summary>
        /// Erstellt ein Kachel-ViewModel für eine Online-Episode.
        /// </summary>
        /// <param name="data">Was die Kachel anzeigt.</param>
        /// <param name="scopeFactory">Für DB-Zugriff beim Setzen des Gehört-Status. Null in Tests.</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel. Nullable, Fallback auf <see cref="SystemClock"/>.</param>
        public OnlineEpisodeCardViewModel(
            OnlineEpisodeCardData data,
            IServiceScopeFactory? scopeFactory = null,
            IClock? clock = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            EpisodeId = data.EpisodeId;
            EpisodeNumber = data.EpisodeNumber;
            Title = data.Title;
            ReleaseDate = data.ReleaseDate;
            ProviderUrl = data.ProviderUrl;
            AppleMusicAlbumId = data.AppleMusicAlbumId;
            SpotifyAlbumId = data.SpotifyAlbumId;
            _scopeFactory = scopeFactory;
            _clock = clock ?? new SystemClock();
            _isCompleted = data.IsCompleted;

            OpenInBrowserCommand = new RelayCommand(OpenInBrowser);
        }

        /// <summary>Datenbank-ID der Episode.</summary>
        public Guid EpisodeId { get; }

        /// <summary>Episodennummer oder null.</summary>
        public int? EpisodeNumber { get; }

        /// <summary>Episodentitel.</summary>
        public string Title { get; }

        /// <summary>Erscheinungsdatum oder null.</summary>
        public DateTime? ReleaseDate { get; }

        /// <summary>URL zum Öffnen der Folge beim Provider. Null bei lokalen Folgen.</summary>
        public Uri? ProviderUrl { get; }

        /// <summary>Apple-Music-Album-ID für Deep-Links. Null → Fallback auf Suchlink.</summary>
        public string? AppleMusicAlbumId { get; }

        /// <summary>Spotify-Album-ID für Deep-Links. Null → Fallback auf Suchlink.</summary>
        public string? SpotifyAlbumId { get; }

        /// <summary>Öffnet die Folge im Standard-Browser beim Provider.</summary>
        public ICommand OpenInBrowserCommand { get; }

        /// <summary>Sichtbarkeit des "Im Browser öffnen"-Buttons.</summary>
        public Visibility OpenInBrowserVisibility =>
            ProviderUrl is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Ob die Episode als gehört markiert ist.
        /// </summary>
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (SetProperty(ref _isCompleted, value))
                {
                    OnPropertyChanged(nameof(CompletedVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Gehört-Häkchens.</summary>
        public Visibility CompletedVisibility =>
            _isCompleted ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Anzeigetext: nur der Titel, da online-importierte Episoden den Albumnamen
        /// als Titel haben (z.B. "125 - Die drei ??? und der Karpaten-Hund").
        /// Die EpisodeNumber ist die Track-Nummer im Album, nicht die Folgennummer;
        /// sie wird deshalb nicht im Anzeigetext dargestellt.
        /// </summary>
        public string DisplayText => Title;

        /// <summary>
        /// Formatiertes Erscheinungsdatum für die Anzeige.
        /// Leerstring wenn kein Datum vorhanden.
        /// </summary>
        public string ReleaseDateText =>
            ReleaseDate?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;

        /// <summary>
        /// Öffnet die Provider-URL im Standard-Browser.
        /// </summary>
        private void OpenInBrowser()
        {
            // Nur http/https-Links im Browser öffnen, das schützt gegen manipulierte
            // ProviderUrls (z.B. file:// oder Custom-Protocol), die per ShellExecute
            // sonst beliebige Programme starten könnten.
            if (!EchoPlay.App.Helpers.SafeUrlLauncher.TryOpenInBrowser(ProviderUrl))
            {
                return;
            }

            // Folge als gehört markieren, denn der Nutzer öffnet die App/Webseite zum Anhören,
            // also gilt sie als gehört. Kann über Kontextmenü rückgängig gemacht werden.
            IsCompleted = true;
            _ = PersistCompletedStatusAsync();
        }

        /// <summary>
        /// Baut die Deep-Link-URL für Apple Music. Mit Album-ID → Direktlink,
        /// ohne → Suchlink als Fallback.
        /// </summary>
        internal string BuildAppleMusicUrl()
        {
            return AppleMusicAlbumId is not null
                ? $"https://music.apple.com/de/album/{AppleMusicAlbumId}"
                : $"https://music.apple.com/de/search?term={Uri.EscapeDataString(Title)}";
        }

        /// <summary>
        /// Baut die Deep-Link-URL für Spotify. Mit Album-ID → Direktlink,
        /// ohne → Suchlink als Fallback.
        /// </summary>
        internal string BuildSpotifyUrl()
        {
            return SpotifyAlbumId is not null
                ? $"https://open.spotify.com/album/{SpotifyAlbumId}"
                : $"https://open.spotify.com/search/{Uri.EscapeDataString(Title)}";
        }

        /// <summary>
        /// Speichert den Gehört-Status in der Datenbank.
        /// Erstellt einen neuen PlaybackState wenn noch keiner existiert.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Speichern des Gehört-Status nebenher: Datenbankfehler dürfen die Kachel nicht zerstören; der Fehler wird protokolliert, die nächste Nutzeraktion kann es erneut versuchen.")]
        internal async Task PersistCompletedStatusAsync()
        {
            if (_scopeFactory is null)
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService stateService = scope.ServiceProvider
                    .GetRequiredService<IPlaybackStateDataService>();

                // Kanonischer Abschluss-Pfad (setzt konsistent CompletedAt) statt externem
                // get-modify-set, das zuvor nur LastPlayedAt setzte und CompletedAt = null ließ.
                await stateService.MarkCompletedAsync(EpisodeId, _clock.UtcNow);
            }
            catch (Exception ex)
            {
                // Das Häkchen bleibt stehen; ohne Protokollzeile fiele der verlorene Stand
                // erst beim nächsten Start auf.
                EchoPlay.App.Infrastructure.ScopedLogger.Create(_scopeFactory, nameof(OnlineEpisodeCardViewModel))?
                    .Warning("Gehört-Status für \"{Title}\" nicht gespeichert: {Reason}", Title, ex.Message);
            }
        }
    }
}
