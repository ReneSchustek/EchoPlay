using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Einstellungsseite mit vier Tabs: Allgemein, Online, Lokal und Protokolle.
    /// Theme-Änderungen werden sofort live angewendet; Speichern persistiert alle anderen Einstellungen.
    /// Ein Sprachwechsel speichert alle Einstellungen und startet die App neu.
    /// Der Online-Tab bietet zusätzlich einen Verbindungstest gegen den aktiven Provider.
    /// Der Protokolle-Tab unterstützt Log-Datei-Auswahl, Live-Ansicht und Aufbewahrungskonfiguration.
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
        /// Wird ausgelöst, wenn der Benutzer ein anderes Theme auswählt.
        /// Die Änderung ist sofort sichtbar – kein Speichern nötig.
        /// Programmatische Synchronisierungen über <see cref="SyncThemeRadioButton"/> werden ignoriert.
        /// Zeigt kurz den Theme-Lade-Overlay im Hauptfenster, damit der Nutzer Feedback erhält.
        /// </summary>
        /// <param name="sender">Der ausgewählte RadioButton.</param>
        /// <param name="e">Ereignisargumente.</param>
        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine Theme-Kachel auswählt.
        /// Der Tag des GridViewItem enthält den Theme-Namen.
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
        /// <param name="sender">Der ausgewählte RadioButton.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnProviderChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag })
            {
                ViewModel.ActiveProviderTag = tag;
            }
        }

        /// <summary>
        /// Öffnet einen Ordner-Picker und übergibt das HWND des Hauptfensters.
        /// </summary>
        /// <param name="sender">Der Browse-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.BrowseLibraryFolderAsync(hWnd));
        }

        /// <summary>
        /// Startet den Sync der lokalen Bibliothek.
        /// </summary>
        /// <param name="sender">Der Synchronisieren-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnSyncClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SyncAsync());
        }

        /// <summary>
        /// Speichert alle Einstellungen in der Datenbank.
        /// Danach wird die StatusBar aktualisiert, damit Provider-Änderungen
        /// (z.B. "Keine") sofort die Sichtbarkeit der Online-Mediathek in der Nav-Leiste ändern.
        /// </summary>
        /// <param name="sender">Der Speichern-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // SaveAsync aktualisiert intern die StatusBar – kein zusätzlicher Refresh nötig.
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SaveAsync());
        }

        /// <summary>
        /// Übernimmt die gewählte Sprache, speichert alle Einstellungen und startet die App neu.
        /// Die Ressourcendateien werden erst nach einem Neustart in der neuen Sprache geladen.
        /// </summary>
        /// <param name="sender">Der Übernehmen-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnLanguageApplyClick(object sender, RoutedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is LanguageOption option)
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ChangeLanguageAsync(option.Code));
            }
        }

        /// <summary>
        /// Startet die Mustererkennung für den konfigurierten Bibliotheksordner.
        /// </summary>
        /// <param name="sender">Der "Muster analysieren"-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnAnalyzePatternClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.AnalyzePatternAsync());
        }

        /// <summary>
        /// Übernimmt das angeklickte Muster in das Episodenmuster-Textfeld.
        /// Das Tag des Buttons enthält das Muster als String.
        /// </summary>
        /// <param name="sender">Der Vorschlag-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnPatternSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pattern })
            {
                ViewModel.ApplyPatternSuggestion(pattern);
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

        // ── Muster-Auswahl-Dialog ────────────────────────────────────────────────

        /// <summary>
        /// Event-Handler für <see cref="SettingsViewModel.PatternSelectionRequested"/>.
        /// Delegiert an den asynchronen Dialog-Methode.
        /// async void ist hier korrekt – WinUI-Event-Handler dürfen keinen Task zurückgeben.
        /// </summary>
        /// <param name="suggestions">Die vom Analyzer vorgeschlagenen Muster.</param>
        private async void OnPatternSelectionRequested(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ShowPatternSelectionDialogAsync(suggestions));
        }

        /// <summary>
        /// Zeigt einen ContentDialog mit RadioButtons für jeden Muster-Vorschlag.
        /// Der Nutzer wählt eines der Muster aus und bestätigt – erst dann wird es übernommen.
        /// </summary>
        /// <param name="suggestions">Die vom Analyzer ermittelten Vorschläge.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        private async Task ShowPatternSelectionDialogAsync(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            StackPanel contentPanel = new() { Spacing = 8 };

            RadioButton? firstButton = null;

            foreach (PatternSuggestionDisplay suggestion in suggestions)
            {
                // Beschreibungstext: Trefferquote in Prozent oder Hinweis auf flache Struktur
                string description = suggestion.IsFlatStructure
                    ? _localizationService.Get("PatternDialogFlatHint")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        _localizationService.Get("PatternDialogMatchFormat"),
                        (int)(suggestion.MatchPercentage * 100));

                StackPanel itemPanel = new() { Spacing = 2 };

                // Muster in Monospace-Schrift – erleichtert das Lesen der Platzhalter
                RadioButton radio = new()
                {
                    Content    = suggestion.Pattern,
                    Tag        = suggestion.Pattern,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    IsChecked  = firstButton is null
                };

                TextBlock descBlock = new()
                {
                    Text       = description,
                    FontSize   = 12,
                    Margin     = new Thickness(28, 0, 0, 0),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };

                itemPanel.Children.Add(radio);
                itemPanel.Children.Add(descBlock);
                contentPanel.Children.Add(itemPanel);

                firstButton ??= radio;
            }

            ContentDialog dialog = new()
            {
                XamlRoot          = XamlRoot,
                Title             = _localizationService.Get("PatternDialogTitle"),
                Content           = contentPanel,
                PrimaryButtonText = _localizationService.Get("PatternDialogApply"),
                CloseButtonText   = _localizationService.Get("PatternDialogCancel")
            };

            Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            // Ausgewählten RadioButton finden und Muster übernehmen
            foreach (UIElement element in contentPanel.Children)
            {
                if (element is not StackPanel panel)
                {
                    continue;
                }

                foreach (UIElement child in panel.Children)
                {
                    if (child is RadioButton { IsChecked: true, Tag: string pattern })
                    {
                        ViewModel.ApplyPatternSuggestion(pattern);
                        return;
                    }
                }
            }
        }

        // ── Log-Viewer ───────────────────────────────────────────────────────────

        /// <summary>
        /// Liest die aktuellen Log-Einträge aus dem Puffer und scrollt ans Ende der Liste.
        /// </summary>
        /// <param name="sender">Der Aktualisieren-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnRefreshLogsClick(object sender, RoutedEventArgs e)
        {
            RefreshLogView();
        }

        /// <summary>
        /// Startet den Live-Timer (2 Sekunden Intervall) für automatisches Log-Refresh.
        /// </summary>
        private void OnLiveViewChecked(object sender, RoutedEventArgs e)
        {
            if (_logLiveTimer is null)
            {
                _logLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _logLiveTimer.Tick += (_, _) => RefreshLogView();
            }

            _logLiveTimer.Start();
            RefreshLogView();
        }

        /// <summary>
        /// Stoppt den Live-Timer.
        /// </summary>
        private void OnLiveViewUnchecked(object sender, RoutedEventArgs e)
        {
            _logLiveTimer?.Stop();
        }

        /// <summary>
        /// Aktualisiert den RichTextBlock mit den aktuellen Log-Einträgen
        /// und scrollt ans Ende. Wird bei manuellem Refresh und Live-Updates aufgerufen.
        /// </summary>
        private void RefreshLogView()
        {
            ViewModel.RefreshLogs();

            // RichTextBlock mit den aktuellen Einträgen befüllen
            LogRichTextBlock.Blocks.Clear();

            Microsoft.UI.Xaml.Documents.Paragraph paragraph = new();

            foreach (string entry in ViewModel.LogEntries)
            {
                paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = entry });
                paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            }

            LogRichTextBlock.Blocks.Add(paragraph);

            // Ans Ende scrollen – neueste Einträge sind unten
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ScrollToVerticalOffset(LogScrollViewer.ScrollableHeight);
        }

        // ── Datenbankpflege ──────────────────────────────────────────────────────

        /// <summary>
        /// Startet die Datenbankbereinigung mit den aktuell konfigurierten Einstellungen.
        /// </summary>
        /// <param name="sender">Der "Jetzt bereinigen"-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnDbMaintenanceClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                await ViewModel.RunMaintenanceAsync();

                // Abschlussdialog – der Nutzer sieht klar, ob die Bereinigung geklappt hat.
                // MaintenanceStatusText enthält bei Erfolg eine Bestätigung, bei Fehler die Meldung.
                ContentDialog resultDialog = new()
                {
                    XamlRoot        = XamlRoot,
                    Title           = _resources.GetString("DatabaseMaintenanceTitle"),
                    Content         = new TextBlock
                    {
                        Text         = ViewModel.MaintenanceStatusText,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    },
                    CloseButtonText = _resources.GetString("CommonCloseButton")
                };

                Helpers.ContentDialogDragHelper.MakeDraggable(resultDialog);
                await resultDialog.ShowAsync();
            });
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der NumberBox in die ViewModel-Property.
        /// NumberBox.Value ist <see langword="double"/> – explizite Konvertierung in <see langword="int"/> nötig.
        /// </summary>
        /// <param name="sender">Die DbPurgeDays-NumberBox.</param>
        /// <param name="args">Enthält den neuen Wert.</param>
        private void OnDbPurgeDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // NaN tritt auf, wenn der Nutzer ein ungültiges Zeichen eingibt – ignorieren
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.DbPurgeDays = (int)args.NewValue;
            }
        }

        /// <summary>
        /// Setzt die Bibliothek zurück – je nach gewähltem Scope: Online, Lokal oder Alle.
        /// </summary>
        private async void OnResetClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Scope aus RadioButtons ermitteln
                int selectedIndex = ResetScopeRadio.SelectedIndex;
                string scopeLabel = selectedIndex switch
                {
                    0 => "Online",
                    1 => "Lokal",
                    _ => "Alle"
                };

                TextBlock contentText = new()
                {
                    Text        = _localizationService.Get("DbResetDialogDescription"),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                };

                ContentDialog dialog = new()
                {
                    XamlRoot          = XamlRoot,
                    Title             = $"{_localizationService.Get("DbResetDialogTitle")} ({scopeLabel})",
                    Content           = contentText,
                    PrimaryButtonText = _localizationService.Get("DbResetDialogConfirm"),
                    CloseButtonText   = _localizationService.Get("PatternDialogCancel")
                };

                Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
                ContentDialogResult result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    await ViewModel.ResetLibraryAsync(selectedIndex);
                }
            });
        }

        // ── RadioButton-Synchronisation ──────────────────────────────────────────

        /// <summary>
        /// Setzt die korrekte Theme-Kachel basierend auf dem gespeicherten Theme-Namen.
        /// Das Guard-Flag <see cref="_isSyncingThemeRadioButtons"/> verhindert, dass das
        /// ausgelöste SelectionChanged-Event einen erneuten Theme-Wechsel verursacht.
        /// </summary>
        /// <param name="themeName">Name des aktiven Themes.</param>
        private void SyncThemeRadioButton(string themeName)
        {
            _isSyncingThemeRadioButtons = true;
            try
            {
                foreach (object item in ThemeGridView.Items)
                {
                    if (item is ThemePreviewViewModel preview && preview.Tag == themeName)
                    {
                        ThemeGridView.SelectedItem = preview;
                        break;
                    }
                }
            }
            finally
            {
                _isSyncingThemeRadioButtons = false;
            }
        }

        /// <summary>
        /// Setzt den korrekten Provider-RadioButton basierend auf dem ViewModel-Tag-String.
        /// </summary>
        /// <param name="providerTag">Provider-Tag aus dem ViewModel: <c>"Spotify"</c>, <c>"AppleMusic"</c> oder leerer String.</param>
        private void SyncProviderRadioButton(string providerTag)
        {
            switch (providerTag)
            {
                case "Spotify":    RadioSpotify.IsChecked    = true; break;
                case "AppleMusic": RadioAppleMusic.IsChecked = true; break;
                case "Both":       RadioBoth.IsChecked       = true; break;
                default:           RadioNone.IsChecked       = true; break;
            }
        }

        /// <summary>
        /// Setzt das ausgewählte Element der Sprach-ComboBox auf den gespeicherten Sprachcode.
        /// </summary>
        /// <param name="languageCode">Der gespeicherte BCP-47-Sprachcode, z.B. "de".</param>
        private void SyncLanguageComboBox(string languageCode)
        {
            foreach (LanguageOption option in ViewModel.AvailableLanguages)
            {
                if (option.Code == languageCode)
                {
                    LanguageComboBox.SelectedItem = option;
                    return;
                }
            }

            // Fallback auf erste Sprache wenn kein passender Eintrag gefunden wurde
            if (ViewModel.AvailableLanguages.Count > 0)
            {
                LanguageComboBox.SelectedItem = ViewModel.AvailableLanguages[0];
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine andere Log-Datei in der ComboBox auswählt.
        /// Die Dateiauswahl löst im ViewModel das Laden des Inhalts aus.
        /// </summary>
        /// <param name="sender">Die Log-Datei-ComboBox.</param>
        /// <param name="e">Enthält die neue Auswahl.</param>
        private void OnLogFileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: LogFileOption option })
            {
                ViewModel.SelectedLogFile = option;
            }
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der Neuerscheinungen-NumberBox in die ViewModel-Property.
        /// NumberBox.Value ist <see langword="double"/> – explizite Konvertierung in <see langword="int"/> nötig.
        /// </summary>
        /// <param name="sender">Die NewReleaseDays-NumberBox.</param>
        /// <param name="args">Enthält den neuen Wert.</param>
        private void OnNewReleaseDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // NaN tritt auf, wenn der Nutzer ein ungültiges Zeichen eingibt – ignorieren
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.NewReleaseDays = (int)args.NewValue;
            }
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der NumberBox in die ViewModel-Property.
        /// NumberBox.Value ist <see langword="double"/> – explizite Konvertierung in <see langword="int"/> nötig.
        /// </summary>
        /// <param name="sender">Die Aufbewahrungszeit-NumberBox.</param>
        /// <param name="args">Enthält den neuen Wert.</param>
        private void OnLogRetentionDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // NaN tritt auf, wenn der Nutzer ein ungültiges Zeichen eingibt – ignorieren
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.LogRetentionDays = (int)args.NewValue;
            }
        }
    }
}
