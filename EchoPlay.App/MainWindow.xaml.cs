using EchoPlay.App.Pages;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App
{
    /// <summary>
    /// Hauptfenster der Anwendung. Enthält die NavigationView-Shell,
    /// den MiniPlayer und die untere Info-Leiste mit Statistiken und Schnellzugriff.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>ViewModel für den MiniPlayer – von XAML über x:Bind referenziert.</summary>
        public MiniPlayerViewModel MiniPlayer { get; }

        /// <summary>ViewModel für die Info-Leiste – von XAML über x:Bind referenziert.</summary>
        public StatusBarViewModel StatusBar { get; }

        private readonly PlayerService _playerService;
        private bool _isSeekingFromSlider;

        /// <summary>
        /// Initialisiert das Hauptfenster und setzt die Fenstergröße.
        /// </summary>
        public MainWindow()
        {
            _playerService        = App.Services.GetRequiredService<PlayerService>();
            MiniPlayer            = App.Services.GetRequiredService<MiniPlayerViewModel>();
            StatusBar             = App.Services.GetRequiredService<StatusBarViewModel>();

            InitializeComponent();

            // Standard-Geschwindigkeit auf 1× vorauswählen (Index 1: 0.75×, 1×, 1.25×, 1.5×, 2×)
            SpeedComboBox.SelectedIndex = 1;

            // Startgröße – groß genug für die NavigationView mit aufgeklapptem Pane
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 750));

            // Icon für Taskleiste und Titelleiste – muss nach InitializeComponent gesetzt werden.
            AppWindow.SetIcon("Assets/favicon.ico");

            // Zurück-Button nach jeder Navigation aktualisieren
            ContentFrame.Navigated += (_, _) => NavView.IsBackEnabled = ContentFrame.CanGoBack;

            // PlayPause-Icon initialisieren – Pause-Glyph, weil der Button immer pausiert/resumt
            MiniPlayer.PropertyChanged += OnMiniPlayerPropertyChanged;

            // Info-Leiste beim Fensterstart befüllen – Fehler sind nicht kritisch
            _ = StatusBar.LoadAsync().ContinueWith(t =>
            {
                if (t.IsFaulted) System.Diagnostics.Trace.WriteLine($"StatusBar.LoadAsync fehlgeschlagen: {t.Exception?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Navigiert beim ersten Laden zur Startseite.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Ereignisargumente.</param>
        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            NavView.SelectedItem = NavStartseite;
            _ = UpdateOnlineVisibilityAsync();
        }

        /// <summary>
        /// Blendet Online-Menüpunkte aus, wenn kein Online-Provider konfiguriert ist.
        /// Prüft <c>AppSettings.ActiveProvider</c> – ist kein Provider gesetzt, werden
        /// die Online-Mediathek und der Online-Tab in der Suche ausgeblendet.
        /// </summary>
        private async Task UpdateOnlineVisibilityAsync()
        {
            try
            {
                using Microsoft.Extensions.DependencyInjection.IServiceScope scope =
                    App.Services.CreateScope();
                EchoPlay.Data.Services.Interfaces.IAppSettingsDataService settingsService =
                    scope.ServiceProvider.GetRequiredService<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>();

                EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();

                // ProviderType.None oder Standard-Wert → kein Online-Provider
                bool hasOnline = settings.ActiveProvider != EchoPlay.Data.Entities.Settings.ProviderType.None;

                NavMediathekOnline.Visibility = hasOnline
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                // Nur-Online-Modus: lokale Mediathek und Tag-Manager ausblenden
                bool hideLocal = settings.OnlineOnlyMode;
                NavMediathekLokal.Visibility = hideLocal
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                NavTagManager.Visibility = hideLocal
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            catch
            {
                // Bei Fehler: Online-Menüpunkte sicherheitshalber ausblenden
                NavMediathekOnline.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Reagiert auf Navigationswechsel in der NavigationView.
        /// Einstellungen erkennt den speziellen Settings-Eintrag über <c>IsSettingsSelected</c>.
        /// Der Mediathek-Elterneintrag hat kein eigenes Tag – nur seine Untereinträge navigieren.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Enthält das ausgewählte Item und ob Einstellungen aktiv sind.</param>
        private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type? pageType = null;

            if (args.IsSettingsSelected)
            {
                pageType = typeof(SettingsPage);
            }
            else if (args.SelectedItem is NavigationViewItem item)
            {
                pageType = (item.Tag as string) switch
                {
                    "Startseite"      => typeof(DashboardPage),
                    "MediathekOnline" => typeof(MediathekOnlinePage),
                    "MediathekLokal"  => typeof(MediathekLokalPage),
                    "TagManager"      => typeof(TagManagerPage),
                    "Suche"           => typeof(SuchePage),
                    "Player"          => typeof(PlayerPage),
                    "Statistik"       => typeof(StatistikPage),
                    "Ueber"           => typeof(UeberPage),
                    _                 => null
                };
            }

            if (pageType is not null)
            {
                await NavigateToAsync(pageType);
            }
        }

        /// <summary>
        /// Navigiert zum angegebenen Page-Typ, sofern dieser nicht bereits aktiv ist.
        /// Prüft vorher, ob die aktuelle Seite ungespeicherte Änderungen hat (z.B. <see cref="SettingsPage"/>).
        /// </summary>
        /// <param name="pageType">Der Typ der Zielseite.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        private async Task NavigateToAsync(Type pageType)
        {
            // Nur identische Seite überspringen – nicht Unterseiten
            if (ContentFrame.Content?.GetType() == pageType)
            {
                return;
            }

            // Prüfung auf ungespeicherte Einstellungen vor dem Verlassen der Settings-Seite
            if (ContentFrame.Content is SettingsPage settingsPage)
            {
                bool canLeave = await settingsPage.CheckUnsavedChangesAsync();
                if (!canLeave)
                {
                    return;
                }
            }

            ContentFrame.Navigate(pageType);
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer den Zurück-Button der NavigationView drückt.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Ereignisargumente.</param>
        private async void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (!ContentFrame.CanGoBack)
            {
                return;
            }

            // Prüfung auf ungespeicherte Einstellungen vor dem Verlassen der Settings-Seite
            if (ContentFrame.Content is SettingsPage settingsPage)
            {
                bool canLeave = await settingsPage.CheckUnsavedChangesAsync();
                if (!canLeave)
                {
                    return;
                }
            }

            ContentFrame.GoBack();
        }

        /// <summary>
        /// Schaltet zwischen Play und Pause um und aktualisiert das Icon.
        /// </summary>
        /// <param name="sender">Der Play/Pause-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (MiniPlayer.IsPlaying)
            {
                _playerService.Pause();
            }
            else
            {
                _playerService.Resume();
            }
        }

        /// <summary>
        /// Springt zu der vom Nutzer gewählten Position im Track.
        /// Programmatische Slider-Updates (via Timer) werden durch das Flag unterdrückt.
        /// </summary>
        /// <param name="sender">Der Seek-Slider.</param>
        /// <param name="e">Enthält den neuen Wert.</param>
        private void OnSeekSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Programmatische Updates durch OnMiniPlayerPropertyChanged ignorieren
            if (_isSeekingFromSlider)
            {
                return;
            }

            MiniPlayer.SeekTo(e.NewValue);
        }

        /// <summary>
        /// Aktualisiert das Play/Pause-Glyph und den Slider-Wert, wenn der PlayerService den Zustand wechselt.
        /// Der Slider wird per Code-Behind gesetzt, um die Rückkopplungsschleife zwischen
        /// PositionSeconds-Bindung und ValueChanged zu vermeiden.
        /// </summary>
        /// <param name="sender">Das MiniPlayerViewModel.</param>
        /// <param name="e">Enthält den Namen der geänderten Property.</param>
        private void OnMiniPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MiniPlayerViewModel.IsPlaying))
            {
                // Pause-Glyph (E769) wenn gespielt wird, Play-Glyph (E768) wenn pausiert
                PlayPauseIcon.Glyph = MiniPlayer.IsPlaying ? "\uE769" : "\uE768";
            }
            else if (e.PropertyName == nameof(MiniPlayerViewModel.PositionSeconds))
            {
                // Flag setzen, damit OnSeekSliderChanged den Seek nicht nochmals auslöst
                _isSeekingFromSlider = true;
                SeekSlider.Value = MiniPlayer.PositionSeconds;
                _isSeekingFromSlider = false;
            }
        }

        /// <summary>
        /// Setzt die Wiedergabegeschwindigkeit aus der ComboBox-Auswahl.
        /// </summary>
        /// <param name="sender">Die Geschwindigkeits-ComboBox.</param>
        /// <param name="e">Enthält das neu gewählte Element.</param>
        private void OnSpeedSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedComboBox.SelectedItem is ComboBoxItem item &&
                double.TryParse(
                    item.Tag?.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double rate))
            {
                MiniPlayer.PlaybackRate = rate;
            }
        }

        /// <summary>Aktiviert den Einschlaf-Timer für 15 Minuten.</summary>
        private void OnSleepTimer15Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(15));

        /// <summary>Aktiviert den Einschlaf-Timer für 30 Minuten.</summary>
        private void OnSleepTimer30Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(30));

        /// <summary>Aktiviert den Einschlaf-Timer für 45 Minuten.</summary>
        private void OnSleepTimer45Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(45));

        /// <summary>Aktiviert den Einschlaf-Timer für 60 Minuten.</summary>
        private void OnSleepTimer60Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(60));

        /// <summary>Deaktiviert den Einschlaf-Timer.</summary>
        private void OnSleepTimerOffClicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(null);

        /// <summary>
        /// Stoppt die Wiedergabe und blendet den MiniPlayer sofort aus.
        /// <see cref="PlayerService.Stop"/> verlagert die Media-Pipeline-Operationen
        /// intern auf einen Hintergrund-Thread, daher ist der Aufruf vom UI-Thread sicher.
        /// </summary>
        private void OnMiniPlayerCloseClick(object sender, RoutedEventArgs e)
        {
            MiniPlayerPanel.Visibility = Visibility.Collapsed;
            MiniPlayer.StopCommand.Execute(null);
        }

        // ── Info-Leiste: Theme und Sprache ───────────────────────────────────────

        /// <summary>
        /// Blendet den Theme-Lade-Overlay ein.
        /// Wird aufgerufen, bevor das neue Theme angewendet wird, damit der Nutzer visuelles Feedback erhält.
        /// </summary>
        internal void ShowThemeOverlay() => ThemeLoadingOverlay.Visibility = Visibility.Visible;

        /// <summary>
        /// Blendet den Theme-Lade-Overlay aus.
        /// Wird aufgerufen, nachdem das neue Theme vollständig angewendet wurde.
        /// </summary>
        internal void HideThemeOverlay() => ThemeLoadingOverlay.Visibility = Visibility.Collapsed;

        /// <summary>
        /// Wechselt das Theme über das MenuFlyout der Info-Leiste.
        /// Das Tag des MenuFlyoutItem enthält den Theme-Namen.
        /// Zeigt kurz einen Lade-Overlay, damit der Nutzer Feedback über den laufenden Wechsel erhält.
        /// </summary>
        /// <param name="sender">Das ausgelöste MenuFlyoutItem.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnStatusBarThemeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string themeName })
            {
                return;
            }

            ShowThemeOverlay();

            // Einen Frame abwarten, damit der Overlay sichtbar ist bevor das Theme umschaltet
            await Task.Delay(50);

            StatusBar.SwitchTheme(themeName);

            // Kurze Pause für den Layout-Pass nach dem Theme-Wechsel
            await Task.Delay(150);

            HideThemeOverlay();
        }

        /// <summary>
        /// Wechselt die Sprache über das MenuFlyout der Info-Leiste und startet die App neu.
        /// Das Tag des MenuFlyoutItem enthält den BCP-47-Sprachcode.
        /// </summary>
        /// <param name="sender">Das ausgelöste MenuFlyoutItem.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnStatusBarLanguageClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string languageCode)
            {
                await StatusBar.ChangeLanguageAsync(languageCode);
            }
        }
    }
}
