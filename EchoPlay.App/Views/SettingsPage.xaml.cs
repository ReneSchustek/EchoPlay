using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Einstellungsseite mit vier Tabs: Allgemein, Online, Lokal und Protokolle.
    /// Theme-Änderungen werden sofort live angewendet; Speichern persistiert alle anderen Einstellungen.
    /// Ein Sprachwechsel speichert alle Einstellungen und startet die App neu.
    /// Der Online-Tab bietet zusätzlich einen Verbindungstest gegen den aktiven Provider.
    /// Der Protokolle-Tab unterstützt Log-Datei-Auswahl, Live-Ansicht und Aufbewahrungskonfiguration.
    /// Datenbank-Pflege, Muster-Auswahl, Log-Viewer und RadioButton-Sync liegen in den Partial-Klassen.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly ILocalizationService _localizationService;
        private static readonly Windows.ApplicationModel.Resources.ResourceLoader _resources =
            Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();
        private DispatcherTimer? _logLiveTimer;

        /// <summary>Alle Theme-Vorschauen für die Farbkacheln in den Einstellungen.</summary>
        public System.Collections.Generic.IReadOnlyList<ThemePreviewViewModel> ThemeOptions { get; } =
            ThemePreviewViewModel.All;

        /// <summary>
        /// Verhindert eine Endlosschleife beim programmatischen Setzen der Theme-Auswahl.
        /// Ohne dieses Flag würde <see cref="SyncThemeRadioButton"/> das SelectionChanged-Event auslösen,
        /// welches wiederum <see cref="OnThemeSelectionChanged"/> und damit <see cref="SettingsViewModel.ApplyTheme"/> aufruft –
        /// was einen erneuten <see cref="SyncThemeRadioButton"/>-Aufruf und einen StackOverflow verursacht.
        /// </summary>
        private bool _isSyncingThemeRadioButtons;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public SettingsViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public SettingsPage()
        {
            ViewModel            = App.Services.GetRequiredService<SettingsViewModel>();
            _localizationService = App.Services.GetRequiredService<ILocalizationService>();
            InitializeComponent();
        }

        /// <summary>
        /// Lädt Einstellungen und setzt die RadioButtons und die Sprach-ComboBox auf den gespeicherten Zustand.
        /// </summary>
        /// <param name="e">Ereignisargumente der Navigation.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ViewModel.PatternSelectionRequested += OnPatternSelectionRequested;

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                await ViewModel.LoadAsync();

                // RadioButtons nachträglich setzen, da x:Bind für diese nicht geeignet ist
                SyncThemeRadioButton(ViewModel.ActiveTheme);
                SyncProviderRadioButton(ViewModel.ActiveProviderTag);

                // Sprach-ComboBox auf die gespeicherte Sprache setzen
                SyncLanguageComboBox(ViewModel.ActiveLanguage);

                // Log-Datei-ComboBox: Live-Option direkt nach dem Laden vorauswählen
                LogFileComboBox.SelectedItem = ViewModel.SelectedLogFile;

                // Erste Log-Einträge laden – der Setter von SelectedLogFile gibt keinen Refresh
                // aus wenn der Wert sich beim Set nicht geändert hat (SetProperty gibt false zurück).
                ViewModel.RefreshLogs();
            });
        }

        /// <summary>
        /// Räumt Event-Abonnements und das ViewModel beim Verlassen der Seite auf.
        /// Die „ungespeicherte Änderungen"-Frage liegt jetzt im <see cref="SettingsViewModel"/>
        /// und wird vom <see cref="MainWindow"/>-Navigation-Guard (<c>INavigationGuard</c>)
        /// vor der Navigation abgefragt.
        /// </summary>
        /// <param name="e">Ereignisargumente der Navigation.</param>
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
            ViewModel.PatternSelectionRequested -= OnPatternSelectionRequested;
            ViewModel.Dispose();
        }

        /// <summary>
        /// Garantiert, dass der Live-Log-Timer beim Verlassen der Seite gestoppt wird —
        /// auch wenn der Nutzer die Live-Checkbox nicht manuell deaktiviert hat.
        /// Ohne diesen Stopp würde der 2-Sekunden-`Tick` nach der Navigation weiterfeuern
        /// und versuchen, auf die bereits entladene `LogRichTextBlock`-Instanz zu schreiben.
        /// </summary>
        /// <param name="e">Ereignisargumente der Navigation.</param>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _logLiveTimer?.Stop();
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine Theme-Kachel auswählt.
        /// Der Tag des GridViewItem enthält den Theme-Namen.
        /// Die Änderung ist sofort sichtbar – kein Speichern nötig.
        /// </summary>
        private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingThemeRadioButtons)
            {
                return;
            }

            if (sender is not GridView gridView || gridView.SelectedItem is not ThemePreviewViewModel selected)
            {
                return;
            }

            string themeName = selected.Tag;

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                MainWindow? mainWindow = App.MainWindow as MainWindow;
                mainWindow?.ShowThemeOverlay();

                await Task.Delay(50);

                ViewModel.ApplyTheme(themeName);

                await Task.Delay(150);

                mainWindow?.HideThemeOverlay();
            });
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer einen anderen Metadaten-Anbieter wählt.
        /// </summary>
        private void OnProviderChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag })
            {
                ViewModel.ActiveProviderTag = tag;
            }
        }

        /// <summary>Öffnet einen Ordner-Picker und übergibt das HWND des Hauptfensters.</summary>
        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.BrowseLibraryFolderAsync(hWnd));
        }

        /// <summary>Startet den Sync der lokalen Bibliothek.</summary>
        private async void OnSyncClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SyncAsync());
        }

        /// <summary>
        /// Speichert alle Einstellungen in der Datenbank.
        /// Danach wird die StatusBar aktualisiert, damit Provider-Änderungen
        /// (z.B. "Keine") sofort die Sichtbarkeit der Online-Mediathek in der Nav-Leiste ändern.
        /// </summary>
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // SaveAsync aktualisiert intern die StatusBar – kein zusätzlicher Refresh nötig.
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SaveAsync());
        }

        /// <summary>
        /// Übernimmt die gewählte Sprache, speichert alle Einstellungen und startet die App neu.
        /// Die Ressourcendateien werden erst nach einem Neustart in der neuen Sprache geladen.
        /// </summary>
        private async void OnLanguageApplyClick(object sender, RoutedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is LanguageOption option)
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ChangeLanguageAsync(option.Code));
            }
        }

        /// <summary>
        /// Verwirft alle nicht gespeicherten Änderungen und lädt die gespeicherten Einstellungen neu.
        /// Der Nutzer bleibt auf der Einstellungsseite – die Navigation wird nicht beeinflusst.
        /// </summary>
        private async void OnCancelClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.LoadAsync());
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine andere Log-Datei in der ComboBox auswählt.
        /// Die Dateiauswahl löst im ViewModel das Laden des Inhalts aus.
        /// </summary>
        private void OnLogFileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: LogFileOption option })
            {
                ViewModel.SelectedLogFile = option;
            }
        }
    }
}
