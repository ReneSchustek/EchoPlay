using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace EchoPlay.App
{
    /// <summary>
    /// Hauptfenster der Anwendung. Enthält die NavigationView-Shell,
    /// den MiniPlayer und die untere Info-Leiste mit Statistiken und Schnellzugriff.
    /// Die Navigation und die Sichtbarkeitslogik der Menüpunkte laufen über
    /// <see cref="MainWindowViewModel"/>; der Code-Behind bleibt auf reine
    /// UI-Adapter beschränkt (Slider-Feedback, Play/Pause-Glyph, Theme-Overlay).
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>ViewModel des Hauptfensters: Shell-Logik und Navigation.</summary>
        public MainWindowViewModel ViewModel { get; }

        /// <summary>ViewModel für den MiniPlayer, von XAML über x:Bind referenziert.</summary>
        public MiniPlayerViewModel MiniPlayer { get; }

        /// <summary>ViewModel für die Info-Leiste, von XAML über x:Bind referenziert.</summary>
        public StatusBarViewModel StatusBar { get; }

        /// <summary>Hinweis zum Abgleich in der Info-Leiste, von XAML über x:Bind referenziert.</summary>
        public ReplicationStatusViewModel ReplicationStatus { get; }

        private readonly PlayerService _playerService;
        private bool _isSeekingFromSlider;

        /// <summary>
        /// Initialisiert das Hauptfenster und setzt die Fenstergröße.
        /// </summary>
        public MainWindow()
        {
            _playerService = App.Services.GetRequiredService<PlayerService>();
            ViewModel = App.Services.GetRequiredService<MainWindowViewModel>();
            MiniPlayer = App.Services.GetRequiredService<MiniPlayerViewModel>();
            StatusBar = App.Services.GetRequiredService<StatusBarViewModel>();
            ReplicationStatus = App.Services.GetRequiredService<ReplicationStatusViewModel>();

            InitializeComponent();

            // NavigationService mit dem Shell-Frame verbinden, genau einmal und direkt nach InitializeComponent.
            NavigationService navigation = App.Services.GetRequiredService<NavigationService>();
            navigation.Initialize(ContentFrame);

            // Standard-Geschwindigkeit auf 1× vorauswählen (Reihenfolge: 0.75×, 1×, 1.25×, 1.5×, 2×)
            const int defaultSpeedIndex = 1;
            SpeedComboBox.SelectedIndex = defaultSpeedIndex;

            // Startgröße, groß genug für die NavigationView mit aufgeklapptem Pane
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 750));

            // Icon für Taskleiste und Titelleiste. Muss nach InitializeComponent gesetzt werden.
            // Absoluter Pfad ab dem App-Verzeichnis: ein relativer Pfad löst gegen das
            // Arbeitsverzeichnis auf und schlägt fehl (kein Taskleisten-Symbol), sobald die App
            // nicht aus ihrem Installationsordner heraus gestartet wird.
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "favicon.ico"));

            // Zurück-Button nach jeder Navigation aktualisieren
            ContentFrame.Navigated += (_, _) => NavView.IsBackEnabled = ContentFrame.CanGoBack;

            // Menüpunkt-Sichtbarkeit an ViewModel-Properties binden,
            // ohne x:Bind, weil NavigationViewItem.Visibility keine BindableCustomProperty ist.
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Symbol der Abspieltaste und Stellung des Positionsreglers folgen dem Mini-Player.
            MiniPlayer.PropertyChanged += OnMiniPlayerPropertyChanged;

            // Info-Leiste beim Fensterstart befüllen. Fehler sind nicht kritisch, aber werden geloggt.
            // Logger wird über die Factory erzeugt; ein Root-ILogger ist nicht im DI-Container registriert.
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                App.Services.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>();
            EchoPlay.Logger.Abstractions.ILogger logger = loggerFactory.CreateLogger("MainWindow");
            _ = StatusBar.LoadAsync().ContinueWith(
                t => logger.Error("StatusBar.LoadAsync fehlgeschlagen", t.Exception),
                // MainWindow-Konstruktor: kein Aufrufer-CT verfügbar; ContinueWith muss
                // unabhängig von Window-Lifecycle laufen, damit StatusBar-Fehler geloggt werden.
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Navigiert beim ersten Laden zur Startseite und lädt die Sichtbarkeits-Einstellungen.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Ereignisargumente.</param>
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                NavView.SelectedItem = NavDashboard;
                await ViewModel.LoadAsync();
                ApplyNavigationItemVisibility();
            });
        }

        /// <summary>
        /// Spiegelt die ViewModel-Sichtbarkeits-Properties auf die NavigationViewItems.
        /// Wird initial und bei jeder Property-Änderung aufgerufen.
        /// </summary>
        private void ApplyNavigationItemVisibility()
        {
            NavOnlineLibrary.Visibility = ViewModel.IsMediathekOnlineVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            NavLocalLibrary.Visibility = ViewModel.IsMediathekLokalVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            NavTagManager.Visibility = ViewModel.IsTagManagerVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Reagiert auf Änderungen der Sichtbarkeits-Properties des ViewModels.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsMediathekOnlineVisible)
                               or nameof(MainWindowViewModel.IsMediathekLokalVisible)
                               or nameof(MainWindowViewModel.IsTagManagerVisible))
            {
                ApplyNavigationItemVisibility();
            }
        }

        /// <summary>
        /// Reagiert auf Navigationswechsel in der NavigationView.
        /// Der eigentliche Seitenwechsel läuft über <see cref="MainWindowViewModel"/>
        /// und den <see cref="INavigationService"/>.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Enthält das ausgewählte Item und ob Einstellungen aktiv sind.</param>
        private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Ungespeicherte Settings-Änderungen prüfen, bevor navigiert wird
                if (!await ConfirmLeaveCurrentPageAsync())
                {
                    return;
                }

                if (args.IsSettingsSelected)
                {
                    ViewModel.NavigateToSettings();
                    return;
                }

                if (args.SelectedItem is NavigationViewItem item)
                {
                    _ = ViewModel.NavigateToMenuTag(item.Tag as string);
                }
            });
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer den Zurück-Button der NavigationView drückt.
        /// </summary>
        /// <param name="sender">Die NavigationView.</param>
        /// <param name="args">Ereignisargumente.</param>
        private async void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (!ContentFrame.CanGoBack)
                {
                    return;
                }

                if (!await ConfirmLeaveCurrentPageAsync())
                {
                    return;
                }

                _ = ViewModel.GoBack();
            });
        }

        /// <summary>
        /// Prüft, ob die aktuelle Seite verlassen werden darf. Statt den konkreten Page-Typ
        /// zu kennen, wird das ViewModel der aktuellen Page über die Page-Property oder
        /// den DataContext gesucht und auf <see cref="INavigationGuard"/> geprüft. Jedes
        /// Page-ViewModel, das ungespeicherte Änderungen hat, kann dieses Interface
        /// implementieren, um das Verlassen zu kontrollieren.
        /// </summary>
        private async Task<bool> ConfirmLeaveCurrentPageAsync()
        {
            INavigationGuard? guard = ResolveNavigationGuard(ContentFrame.Content);
            if (guard is null)
            {
                return true;
            }

            return await guard.CanLeaveAsync();
        }

        /// <summary>
        /// Extrahiert den <see cref="INavigationGuard"/> aus dem aktuellen Page-Inhalt.
        /// EchoPlay setzt das ViewModel meist als <c>Page.ViewModel</c>-Property, fällt
        /// aber bei Bedarf auf den <c>DataContext</c> zurück.
        /// </summary>
        private static INavigationGuard? ResolveNavigationGuard(object? pageContent)
        {
            if (pageContent is null)
            {
                return null;
            }

            // Primär: ViewModel-Property via Reflection (EchoPlay-Konvention)
            System.Reflection.PropertyInfo? vmProp = pageContent.GetType().GetProperty("ViewModel");
            object? viewModel = vmProp?.GetValue(pageContent);
            if (viewModel is INavigationGuard guardFromProperty)
            {
                return guardFromProperty;
            }

            // Fallback: DataContext eines FrameworkElements
            if (pageContent is FrameworkElement element && element.DataContext is INavigationGuard guardFromContext)
            {
                return guardFromContext;
            }

            return null;
        }

    }
}
