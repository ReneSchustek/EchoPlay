using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert ein einzelnes Suchergebnis auf der Suche-Seite.
    /// Enthält Metadaten, Import-Status und den Befehl zum Importieren der Serie.
    /// </summary>
    public sealed class SearchResultViewModel : ObservableObject
    {
        private readonly ImportSeries _importSeries;
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly CancellationToken _cancellationToken;
        private readonly SucheViewModel? _parentViewModel;
        private readonly Func<Task>? _onImportCompleted;

        private bool _isImported;
        private bool _isImporting;
        private bool _isSelected;
        private bool _isBrightCover;

        /// <summary>
        /// Initialisiert das ViewModel mit Suchergebnis und benötigten Services.
        /// </summary>
        /// <param name="importSeries">Das Suchergebnis aus der externen API.</param>
        /// <param name="isAlreadyImported">Ob die Serie bereits in der lokalen Datenbank vorhanden ist.</param>
        /// <param name="importService">Führt den tatsächlichen Import durch.</param>
        /// <param name="errorDialogService">Zeigt Fehlermeldungen an.</param>
        /// <param name="localizationService">Für lokalisierte Fehlertexte.</param>
        /// <param name="backgroundCoverService">
        /// Lädt das Cover über die zentrale Cover-Pipeline (DB-First, dann Provider-URL über
        /// <c>IHostRateLimiter</c> mit Foreground-Priorität). Null deaktiviert das Cover-Laden.
        /// </param>
        /// <param name="cancellationToken">
        /// Abbruch-Token, das vom Eltern-VM beim Start einer neuen Suche oder beim Verlassen
        /// der Page abgebrochen wird. Hält verwaiste HTTP-Requests aus alten Suchläufen draußen.
        /// </param>
        /// <param name="parentViewModel">
        /// Optionale Referenz auf das übergeordnete SucheViewModel – wird nach
        /// erfolgreichem Hinzufügen benachrichtigt, um den Erfolgshinweis zu zeigen.
        /// </param>
        /// <param name="onImportCompleted">
        /// Optionaler Callback nach erfolgreichem Import – z.B. zum Neuladen der Serienliste
        /// in der Online-Mediathek. Wird zusätzlich zu <paramref name="parentViewModel"/> aufgerufen.
        /// </param>
        public SearchResultViewModel(
            ImportSeries importSeries,
            bool isAlreadyImported,
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            BackgroundCoverService? backgroundCoverService = null,
            SucheViewModel? parentViewModel = null,
            Func<Task>? onImportCompleted = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(importSeries);
            _importSeries = importSeries;
            _importService = importService;
            _errorDialogService = errorDialogService;
            _localizationService = localizationService;
            _backgroundCoverService = backgroundCoverService;
            _cancellationToken = cancellationToken;
            _parentViewModel = parentViewModel;
            _onImportCompleted = onImportCompleted;
            _isImported = isAlreadyImported;

            // Bei Album-Ergebnissen: Künstlername als Untertitel anzeigen
            Title = importSeries.IsAlbumResult && !string.IsNullOrEmpty(importSeries.ArtistName)
                ? $"{importSeries.ArtistName} – {importSeries.Title}"
                : importSeries.Title;
            Description = importSeries.Description;
            IsHoerspiel = importSeries.IsHoerspiel;
            IsAlbumResult = importSeries.IsAlbumResult;

            // Cover einmal über die zentrale Pipeline laden, BitmapImage erstellen UND Helligkeit analysieren
            if (_backgroundCoverService is not null && !string.IsNullOrEmpty(importSeries.CoverImageUrl))
            {
                CoverLoadTask = LoadCoverAndAnalyzeAsync(importSeries.CoverImageUrl);
            }

            ImportCommand = new RelayCommand(() => _ = ImportAsync());
        }

        /// <summary>
        /// Task der laufenden Cover-Lade-Operation. Tests warten darauf, um determinismus
        /// zu garantieren; Produktionscode lässt den Task fire-and-forget laufen.
        /// </summary>
        internal Task? CoverLoadTask { get; }

        /// <summary>Titel der Hörspielserie.</summary>
        public string Title { get; }

        /// <summary>Optionale Kurzbeschreibung der Serie.</summary>
        public string? Description { get; }

        private BitmapImage? _coverImage;

        /// <summary>
        /// Coverbild der Serie – wird asynchron aus den heruntergeladenen Bytes erstellt.
        /// Null zeigt den Platzhalter an.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            private set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    OnPropertyChanged(nameof(NoCoverVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Platzhalters wenn kein Cover vorhanden.</summary>
        public Visibility NoCoverVisibility =>
            _coverImage is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt das geladene <see cref="BitmapImage"/> frei, sobald die Trefferliste
        /// ersetzt oder die Suche-Seite verlassen wird. Ohne diesen Schritt halten
        /// alte Trefferkacheln ihre Cover-Bytes bis zum nächsten GC-Lauf am Heap fest.
        /// </summary>
        public void ClearCoverImage()
        {
            CoverImage = null;
        }

        /// <summary>
        /// Gibt an, ob der Bereich oben links im Cover hell ist.
        /// Steuert die Sichtbarkeit des hellen bzw. dunklen Checkbox-Rahmens im XAML.
        /// Standard: false (dunkles Cover → weißer Rahmen).
        /// </summary>
        public bool IsBrightCover
        {
            get => _isBrightCover;
            private set
            {
                if (SetProperty(ref _isBrightCover, value))
                {
                    OnPropertyChanged(nameof(DarkBorderVisibility));
                    OnPropertyChanged(nameof(LightBorderVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit der dunklen Checkbox (auf hellem Cover).</summary>
        public Visibility DarkBorderVisibility =>
            _isBrightCover && !_isImported ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit der hellen Checkbox (auf dunklem Cover oder Platzhalter).</summary>
        public Visibility LightBorderVisibility =>
            !_isBrightCover && !_isImported ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob die Heuristik die Serie als Hörspiel erkannt hat.
        /// Dient als visueller Hinweis, nicht als Filter.
        /// </summary>
        public bool IsHoerspiel { get; }

        /// <summary>
        /// Gibt an, ob dieses Ergebnis ein Album (einzelne Folge) statt einer Serie ist.
        /// Album-Ergebnisse zeigen den Titel als "Künstler – Albumname".
        /// </summary>
        public bool IsAlbumResult { get; }

        /// <summary>
        /// Gibt an, ob die Serie bereits in der lokalen Bibliothek vorhanden ist.
        /// Ändert sich nach einem erfolgreichen Import.
        /// </summary>
        public bool IsImported
        {
            get => _isImported;
            private set
            {
                if (SetProperty(ref _isImported, value))
                {
                    OnPropertyChanged(nameof(ImportButtonVisibility));
                    OnPropertyChanged(nameof(AlreadyImportedVisibility));
                    OnPropertyChanged(nameof(DarkBorderVisibility));
                    OnPropertyChanged(nameof(LightBorderVisibility));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob gerade ein Import läuft.
        /// Während des Imports ist der Import-Button deaktiviert.
        /// </summary>
        public bool IsImporting
        {
            get => _isImporting;
            private set
            {
                if (SetProperty(ref _isImporting, value))
                {
                    ((RelayCommand)ImportCommand).SetEnabled(!value);
                    OnPropertyChanged(nameof(ImportButtonVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Import-Buttons: sichtbar solange die Serie weder importiert
        /// noch gerade importiert wird. Während des Imports zeigt der ProgressRing den Fortschritt.
        /// </summary>
        public Visibility ImportButtonVisibility =>
            _isImported || _isImporting ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Sichtbarkeit des "Bereits importiert"-Texts: sichtbar nach erfolgreichem Import.
        /// </summary>
        public Visibility AlreadyImportedVisibility =>
            _isImported ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob das Suchergebnis für den Batch-Import ausgewählt ist.
        /// Wird über die Checkbox auf der Kachel gesteuert.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Anzeigename der Quelle, aus der dieses Ergebnis stammt.
        /// "AppleMusic" wird als "Apple Music" (mit Leerzeichen) dargestellt,
        /// alle anderen Werte ("Spotify", "Lokal") bleiben unverändert.
        /// </summary>
        public string SourceLabel => _importSeries.Source switch
        {
            "AppleMusic" => "Apple Music",
            _ => _importSeries.Source
        };

        /// <summary>Startet den Import der Serie in die lokale Bibliothek.</summary>
        public ICommand ImportCommand { get; }

        /// <summary>
        /// Führt den Import durch und aktualisiert danach den Status im ViewModel.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Import einer Trefferkarte: HTTP-/Parser-/DB-Fehler werden als Nutzer-Status gespiegelt, damit der Suche-Ergebnis-Flow nicht abbricht.")]
        private async Task ImportAsync()
        {
            if (_isImporting)
            {
                return;
            }

            using IDisposable ua = UserActionScope.BeginUserAction("ImportSeries");
            IsImporting = true;

            try
            {
                _ = await _importService.ImportAsync(_importSeries);
                // Erst nach erfolgreichem Import den Status setzen – kein optimistisches Update
                IsImported = true;
                _parentViewModel?.NotifySeriesAdded();

                // Callback für die aufrufende Seite – z.B. Online-Mediathek Serienliste aktualisieren
                if (_onImportCompleted is not null)
                {
                    await _onImportCompleted();
                }
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineImportFailedTitle"), ex.Message);
            }
            finally
            {
                IsImporting = false;
            }
        }

        /// <summary>
        /// Lädt das Cover einmal über die zentrale Cover-Pipeline (DB-First, dann
        /// Provider-URL mit Foreground-Priorität) und nutzt die Bytes für:
        /// 1. BitmapImage-Erstellung (Anzeige)
        /// 2. Helligkeitsanalyse oben links (Checkbox-Rahmenfarbe)
        /// Vermeidet doppelten Download und sichert Rate-Limiter-Konformität gegenüber
        /// dem direkten <c>HttpClient</c>-Aufruf, der bei vielen parallelen Treffer-
        /// Karten den Provider drosseln würde.
        /// Die WinRT-COM-Typen für die Analyse leben in <see cref="Services.CoverBrightnessAnalyzer"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Download + Helligkeits-Analyse für eine Trefferkarte: HTTP-/Bild-Dekodier-Fehler dürfen die Kachel nicht zerstören – der Platzhalter bleibt stehen.")]
        private async Task LoadCoverAndAnalyzeAsync(string coverUrl)
        {
            try
            {
                byte[]? coverBytes = await _backgroundCoverService!
                    .RequestCoverForSearchResultAsync(
                        _importSeries.Source,
                        _importSeries.SourceSeriesId,
                        coverUrl,
                        _cancellationToken)
                    .ConfigureAwait(true);

                if (coverBytes is null || coverBytes.Length == 0) return;

                // BitmapImage aus den Bytes erstellen (UI-Thread nötig)
                Microsoft.UI.Xaml.Media.Imaging.BitmapImage image = new();
                using Windows.Storage.Streams.InMemoryRandomAccessStream stream = new();
                _ = await stream.WriteAsync(coverBytes.AsBuffer());
                stream.Seek(0);
                await image.SetSourceAsync(stream);
                CoverImage = image;

                // Helligkeit aus den gleichen Bytes analysieren
                bool? isBright = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(coverBytes);

                if (isBright.HasValue)
                {
                    IsBrightCover = isBright.Value;
                }
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Nutzer hat eine neue Suche gestartet oder die Page verlassen.
            }
            catch (Exception)
            {
                // Netzwerk-/Bild-Dekodier-Fehler → Platzhalter + Standard-Rahmenfarbe
            }
        }
    }
}
