using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Zeigt die lokale Mediathek als dynamisches Akkordeon-Layout.
    /// Serien erscheinen als Cover-Kachelgrid. Nach Auswahl einer Serie klappt der
    /// Folgen-Bereich direkt nach der Reihe der gewählten Kachel auf. Bei Auswahl
    /// einer Folge erscheinen die Tracks in einer Spalte rechts daneben.
    /// Navigation zum Tag-Manager wird über das <see cref="MediathekLokalViewModel.NavigateToTagManagerRequested"/>-Event
    /// ausgelöst – ViewModels navigieren nicht selbst, die Page-Ebene übernimmt das.
    /// Kontextmenü-Handler, Cover- und Fehlende-Folgen-Dialoge liegen in den Partial-Klassen.
    /// </summary>
    public sealed partial class MediathekLokalPage : Page
    {
        // Akkordeon-Split-Handler kapselt die Top/Bottom-Aufteilung der Serien-Kacheln
        // und das Rekursions-Flag (vorher direkt in der Page geführt).
        private readonly Helpers.AccordionSplitHandler<LocalArtistCardViewModel> _splitHandler;

        private static readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();

        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public MediathekLokalViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public MediathekLokalPage()
        {
            ViewModel = App.Services.GetRequiredService<MediathekLokalViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            _splitHandler = new Helpers.AccordionSplitHandler<LocalArtistCardViewModel>(
                SeriesTopGrid,
                SeriesBottomGrid,
                () => ViewModel.Artists,
                () => ViewModel.SelectedArtistIndex,
                () => Math.Max(Helpers.AccordionSplitHelper.SeriesTileSlotWidth, ActualWidth - 16));
        }

        /// <summary>
        /// Lädt Bibliothekspfad und Serienliste beim Navigieren zur Seite.
        /// Abonniert außerdem das Tag-Manager-, AddFolder- und PropertyChanged-Event.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Nur-Online-Modus-Check liegt im ViewModel; bei aktivem Modus navigiert das ViewModel zurück.
                if (!await ViewModel.InitializeAsync())
                {
                    return;
                }

                ViewModel.NavigateToTagManagerRequested += OnNavigateToTagManagerRequested;
                ViewModel.AddFolderRequested += OnAddFolderRequested;
                ViewModel.MissingEpisodesResolved += OnMissingEpisodesResolved;
                ViewModel.MissingEpisodesModeRequested += OnMissingEpisodesModeRequested;
                ViewModel.AllSeriesCheckCompleted += OnAllSeriesCheckCompleted;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                SizeChanged += OnPageSizeChanged;
                EpisodeAccordion.GridView.SelectionChanged += OnEpisodeSelectionChanged;
                ViewModel.Activate();
                await ViewModel.LoadAsync();
            });
        }

        /// <summary>
        /// Deabonniert alle Events beim Verlassen der Seite,
        /// um Memory-Leaks durch hängende Event-Handler zu verhindern.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.NavigateToTagManagerRequested -= OnNavigateToTagManagerRequested;
            ViewModel.AddFolderRequested -= OnAddFolderRequested;
            ViewModel.MissingEpisodesResolved -= OnMissingEpisodesResolved;
            ViewModel.MissingEpisodesModeRequested -= OnMissingEpisodesModeRequested;
            ViewModel.AllSeriesCheckCompleted -= OnAllSeriesCheckCompleted;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            SizeChanged -= OnPageSizeChanged;
            EpisodeAccordion.GridView.SelectionChanged -= OnEpisodeSelectionChanged;
            ViewModel.Deactivate();
            // VM disposed — Scan-Event-Subscriptions, Sub-VM-Ketten und Koordinatoren freigeben.
            ViewModel.Dispose();
        }

        /// <summary>
        /// Reagiert auf Änderungen an <see cref="MediathekLokalViewModel.Artists"/>
        /// oder <see cref="MediathekLokalViewModel.SelectedArtistIndex"/> und aktualisiert
        /// die Aufteilung der Serien-Grids über den <see cref="_splitHandler"/>.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MediathekLokalViewModel.Artists)
                               or nameof(MediathekLokalViewModel.SelectedArtistIndex))
            {
                _splitHandler.UpdateSplit();
            }
        }

        /// <summary>
        /// Berechnet den Split bei Fenstergrößenänderungen neu, damit das Akkordeon
        /// immer am Ende der richtigen Kachelreihe erscheint.
        /// </summary>
        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _splitHandler.HandleSizeChanged();
        }

        /// <summary>
        /// Wird bei jeder Textänderung im lokalen Suchfeld ausgelöst.
        /// Die Filterung erfolgt clientseitig im ViewModel über <see cref="MediathekLokalViewModel.LocalSearchText"/>.
        /// </summary>
        private void OnLocalSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // AutoSuggestBox feuert TextChanged auch bei programmatischen Änderungen –
            // nur bei Nutzereingabe den Filter anwenden, um unnötige Neuberechnungen zu vermeiden.
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.LocalSearchText = sender.Text;
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer eine Serien-Kachel auswählt.
        /// Lädt die Folgen der gewählten Serie und aktualisiert den Split.
        /// </summary>
        private async void OnArtistSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_splitHandler.IsUpdating) return;

            if (sender is GridView { SelectedItem: LocalArtistCardViewModel artist })
            {
                EpisodeAccordion.GridView.SelectedItem = null;
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SelectArtistAsync(artist));
                // UpdateSplit wird durch PropertyChanged auf SelectedArtistIndex ausgelöst
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer eine Folgen-Kachel auswählt.
        /// Lädt die Tracks der gewählten Folge.
        /// </summary>
        private async void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is GridView { SelectedItem: LocalEpisodeCardViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SelectEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Öffnet den Ordnerpicker und speichert den gewählten Bibliothekspfad.
        /// Das HWND muss manuell aus dem Hauptfenster abgefragt werden, da WinRT-Picker
        /// keinen direkten Window-Zugriff haben.
        /// </summary>
        private async void OnPickFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.PickFolderAsync(handle));
        }

        /// <summary>
        /// Öffnet den Ordnerpicker, damit der Nutzer direkt einen Serienordner hinzufügen kann.
        /// </summary>
        private async void OnAddFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.AddFolderAsync(handle));
        }

        /// <summary>
        /// Reagiert auf das <see cref="MediathekLokalViewModel.AddFolderRequested"/>-Event.
        /// Das ViewModel selbst kennt das HWND nicht – die Page liefert es.
        /// </summary>
        private async void OnAddFolderRequested()
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.AddFolderAsync(handle));
        }

        /// <summary>
        /// Schließt den Folgenbereich komplett – setzt die Serienauswahl zurück.
        /// Die Serien-Kacheln füllen wieder die volle Breite, kein Akkordeon sichtbar.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            ViewModel.DeselectArtist();
        }

        /// <summary>Tab-Wechsel: reguläre Folgen anzeigen.</summary>
        private void OnEpisodeTabRegularChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 0;
        }

        /// <summary>Tab-Wechsel: Sonderfolgen anzeigen.</summary>
        private void OnEpisodeTabSpecialChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 1;
        }

        /// <summary>
        /// Navigiert zum Tag-Manager mit dem Ordnerpfad als Parameter.
        /// Der Tag-Manager öffnet daraufhin alle Audiodateien des übergebenen Ordners.
        /// </summary>
        private void OnNavigateToTagManagerRequested(string folderPath)
        {
            _navigationService.NavigateTo(NavigationTarget.TagManager, folderPath);
        }

        /// <summary>
        /// Öffnet den Bilddatei-Picker über den gemeinsamen <see cref="Helpers.ImageFilePicker"/>.
        /// </summary>
        private static Task<byte[]?> PickImageFileAsync()
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            return Helpers.ImageFilePicker.PickAsync(handle);
        }
    }
}
