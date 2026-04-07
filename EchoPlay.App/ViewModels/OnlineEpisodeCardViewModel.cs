using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Episode in der Online-Mediathek.
    /// Zeigt Titel, Nummer, Erscheinungsdatum und Cover.
    /// Online-Episoden haben keine lokalen Tracks – stattdessen gibt es
    /// einen „Im Browser öffnen"-Button für den Direktzugriff beim Provider.
    /// </summary>
    public sealed class OnlineEpisodeCardViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory? _scopeFactory;
        private BitmapImage? _coverImage;
        private bool _isCompleted;

        /// <summary>
        /// Erstellt ein Kachel-ViewModel für eine Online-Episode.
        /// </summary>
        /// <param name="episodeId">Datenbank-ID der Episode.</param>
        /// <param name="episodeNumber">Episodennummer oder null.</param>
        /// <param name="title">Episodentitel.</param>
        /// <param name="releaseDate">Erscheinungsdatum oder null.</param>
        /// <param name="isCompleted">Ob die Episode als gehört markiert ist.</param>
        /// <param name="providerUrl">URL zum Öffnen beim Provider oder null.</param>
        /// <param name="scopeFactory">Für DB-Zugriff beim Setzen des Gehört-Status. Null in Tests.</param>
        public OnlineEpisodeCardViewModel(
            Guid episodeId,
            int? episodeNumber,
            string title,
            DateTime? releaseDate = null,
            bool isCompleted = false,
            string? providerUrl = null,
            IServiceScopeFactory? scopeFactory = null)
        {
            EpisodeId      = episodeId;
            EpisodeNumber  = episodeNumber;
            Title          = title;
            ReleaseDate    = releaseDate;
            ProviderUrl    = providerUrl;
            _scopeFactory  = scopeFactory;
            _isCompleted   = isCompleted;

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
        public string? ProviderUrl { get; }

        /// <summary>Öffnet die Folge im Standard-Browser beim Provider.</summary>
        public ICommand OpenInBrowserCommand { get; }

        /// <summary>Sichtbarkeit des "Im Browser öffnen"-Buttons.</summary>
        public Visibility OpenInBrowserVisibility =>
            !string.IsNullOrEmpty(ProviderUrl) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Cover-Bild – wird asynchron nachgeladen.
        /// Null zeigt den Platzhalter an.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    OnPropertyChanged(nameof(NoCoverVisibility));
                }
            }
        }

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

        /// <summary>Sichtbarkeit des Platzhalters wenn kein Cover vorhanden.</summary>
        public Visibility NoCoverVisibility =>
            _coverImage is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Gehört-Häkchens.</summary>
        public Visibility CompletedVisibility =>
            _isCompleted ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Anzeigetext: nur der Titel, da online-importierte Episoden den Albumnamen
        /// als Titel haben (z.B. "125 - Die drei ??? und der Karpaten-Hund").
        /// Die EpisodeNumber ist die Track-Nummer im Album, nicht die Folgennummer –
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
            if (string.IsNullOrEmpty(ProviderUrl)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName        = ProviderUrl,
                UseShellExecute = true
            });

            // Folge als gehört markieren – der Nutzer öffnet die App/Webseite zum Anhören,
            // also gilt sie als gehört. Kann über Kontextmenü rückgängig gemacht werden.
            IsCompleted = true;
            _ = PersistCompletedStatusAsync();
        }

        /// <summary>
        /// Speichert den Gehört-Status in der Datenbank.
        /// Erstellt einen neuen PlaybackState wenn noch keiner existiert.
        /// </summary>
        private async Task PersistCompletedStatusAsync()
        {
            if (_scopeFactory is null) return;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService stateService = scope.ServiceProvider
                    .GetRequiredService<IPlaybackStateDataService>();

                // Prüfen ob bereits ein PlaybackState existiert
                PlaybackState? existing = await stateService.GetByEpisodeIdAsync(EpisodeId);

                if (existing is not null)
                {
                    existing.IsCompleted = true;
                    existing.LastPlayedAt = DateTime.UtcNow;
                    await stateService.UpdateAsync(existing);
                }
                else
                {
                    PlaybackState newState = new()
                    {
                        EpisodeId    = EpisodeId,
                        IsCompleted  = true,
                        LastPlayedAt = DateTime.UtcNow,
                        LastPosition = TimeSpan.Zero
                    };
                    await stateService.AddAsync(newState);
                }
            }
            catch (Exception)
            {
                // DB-Fehler beim Status-Setzen ist nicht kritisch – UI zeigt Häkchen trotzdem
            }
        }
    }
}
