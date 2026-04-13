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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
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
            ViewModel          = App.Services.GetRequiredService<MediathekLokalViewModel>();
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
                ViewModel.AddFolderRequested            += OnAddFolderRequested;
                ViewModel.MissingEpisodesResolved       += OnMissingEpisodesResolved;
                ViewModel.MissingEpisodesModeRequested += OnMissingEpisodesModeRequested;
                ViewModel.AllSeriesCheckCompleted       += OnAllSeriesCheckCompleted;
                ViewModel.PropertyChanged               += OnViewModelPropertyChanged;
                SizeChanged                              += OnPageSizeChanged;
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
            ViewModel.AddFolderRequested            -= OnAddFolderRequested;
            ViewModel.MissingEpisodesResolved       -= OnMissingEpisodesResolved;
            ViewModel.MissingEpisodesModeRequested -= OnMissingEpisodesModeRequested;
            ViewModel.AllSeriesCheckCompleted       -= OnAllSeriesCheckCompleted;
            ViewModel.PropertyChanged               -= OnViewModelPropertyChanged;
            SizeChanged                              -= OnPageSizeChanged;
            EpisodeAccordion.GridView.SelectionChanged -= OnEpisodeSelectionChanged;
            ViewModel.Deactivate();
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
        /// Markiert alle Episoden der gewählten Serie als gehört.
        /// Die SeriesId wird über das Tag-Property des MenuFlyoutItem übergeben,
        /// weil x:Bind innerhalb eines DataTemplate nur auf den DataType-Kontext zugreifen kann –
        /// nicht auf das übergeordnete ViewModel.
        /// </summary>
        /// <summary>
        /// Navigiert zur Serien-Detailansicht.
        /// </summary>
        private void OnSeriesDetailsClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer lokalen Serie um.
        /// Beim Aktivieren wird sofort ein iTunes-Check für diese Serie ausgelöst,
        /// damit die Neuerscheinungen beim nächsten Dashboard-Besuch bereitstehen.
        /// </summary>
        private async void OnToggleWatchSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                bool isChecked = item.IsChecked;
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ToggleWatchAsync(seriesId, isChecked));
            }
        }

        private async void OnMarkAllAsReadClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkAllAsReadAsync(seriesId));
            }
        }

        /// <summary>
        /// Löscht den Import einer Serie nach Bestätigung durch den Nutzer.
        /// Die SeriesId wird über das Tag-Property des MenuFlyoutItem übergeben.
        /// </summary>
        private async void OnRemoveSeriesFromLibraryClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.DeleteSeriesFromLibraryAsync(seriesId));
            }
        }

        private async void OnDeleteSeriesFromDiskClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.DeleteSeriesFromDiskAsync(seriesId, card?.LocalFolderPath));
            }
        }

        /// <summary>
        /// Zeigt eine ContentDialog-Liste der Episoden, die lokal noch nicht gefunden wurden.
        /// Wird ausgelöst wenn der Nutzer "Fehlende Folgen ermitteln" im Kontextmenü wählt.
        /// Leere Liste → Dialog informiert, dass alle Episoden vorhanden sind.
        /// </summary>
        /// <summary>
        /// Zeigt einen Drei-Optionen-Dialog vor der Fehlende-Folgen-Prüfung:
        /// Online + Offline, Nur offline oder Abbrechen.
        /// </summary>
        private async Task<MissingEpisodesMode> OnMissingEpisodesModeRequested()
        {
            using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
            if (guard is null) return MissingEpisodesMode.Cancel;

            ContentDialog dialog = new()
            {
                XamlRoot           = XamlRoot,
                Title              = _resources.GetString("MissingEpisodesDialogTitle"),
                Content            = _resources.GetString("MissingEpisodesDialogContent"),
                PrimaryButtonText  = _resources.GetString("MissingEpisodesOnlineButton"),
                SecondaryButtonText = _resources.GetString("MissingEpisodesOfflineButton"),
                CloseButtonText    = _resources.GetString("CommonCancelButton")
            };

            try
            {
                Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
                ContentDialogResult result = await dialog.ShowAsync();

                return result switch
                {
                    ContentDialogResult.Primary   => MissingEpisodesMode.WithOnline,
                    ContentDialogResult.Secondary => MissingEpisodesMode.OfflineOnly,
                    _                             => MissingEpisodesMode.Cancel
                };
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return MissingEpisodesMode.Cancel;
            }
        }

        private async void OnMissingEpisodesResolved(IReadOnlyList<string> episodeTitles)
        {
            using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
            if (guard is null) return;

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                string content;

                if (episodeTitles.Count == 0)
                {
                    content = _resources.GetString("MissingEpisodesAllPresent");
                }
                else
                {
                    StringBuilder builder = new();
                    foreach (string title in episodeTitles)
                    {
                        _ = builder.AppendLine(title);
                    }
                    content = builder.ToString().TrimEnd();
                }

                ContentDialogResult result = await Helpers.ScrollableTextDialog.ShowAsync(
                    XamlRoot,
                    title: _resources.GetString("MissingEpisodesResultTitle"),
                    content: content,
                    primaryButtonText: _resources.GetString("CommonSaveAsTxtButton"));

                if (result == ContentDialogResult.Primary)
                {
                    await SaveReportAsTxtAsync(content);
                }
            });
        }

        /// <summary>
        /// Öffnet das Kontextmenü-Dialogfeld für fehlende Folgen der gewählten Serie.
        /// Die SeriesId wird über das Tag-Property des MenuFlyoutItem übergeben.
        /// </summary>
        private async void OnShowMissingEpisodesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid seriesId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ShowMissingEpisodesAsync(seriesId));
            }
        }

        /// <summary>
        /// Zeigt den Gesamtbericht der Fehlende-Folgen-Prüfung in einem Dialog an.
        /// Bietet einen "Als TXT speichern"-Button, der den Bericht über einen
        /// FileSavePicker als Textdatei exportiert.
        /// </summary>
        private async void OnAllSeriesCheckCompleted(EchoPlay.Core.Models.MissingEpisodesReport report)
        {
            using Helpers.DialogReentrancyGuard? guard = Helpers.DialogReentrancyGuard.TryAcquire();
            if (guard is null) return;

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                string reportText = EchoPlay.Core.Models.MissingEpisodesReportFormatter.FormatAsText(report);

                ContentDialogResult result = await Helpers.ScrollableTextDialog.ShowAsync(
                    XamlRoot,
                    title: _resources.GetString("MissingEpisodesAllSeriesTitle"),
                    content: reportText,
                    primaryButtonText: _resources.GetString("CommonSaveAsTxtButton"),
                    maxHeight: 500,
                    useMonospace: true);

                if (result == ContentDialogResult.Primary)
                {
                    await SaveReportAsTxtAsync(reportText);
                }
            });
        }

        /// <summary>
        /// Speichert den Berichtstext über einen FileSavePicker als TXT-Datei.
        /// Der Nutzer kann den Speicherort frei wählen.
        /// </summary>
        private static async Task SaveReportAsTxtAsync(string reportText)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            Windows.Storage.Pickers.FileSavePicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = $"Fehlende-Folgen-{DateTime.Now:yyyy-MM-dd}";
            ResourceLoader resources = ResourceLoader.GetForViewIndependentUse();
            picker.FileTypeChoices.Add(resources.GetString("CommonTextFileFilter"), [".txt"]);

            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();

            if (file is not null)
            {
                await Windows.Storage.FileIO.WriteTextAsync(file, reportText);
            }
        }

        /// <summary>
        /// Navigiert zum Tag-Manager mit dem Ordnerpfad als Parameter.
        /// Der Tag-Manager öffnet daraufhin alle Audiodateien des übergebenen Ordners.
        /// </summary>
        private void OnNavigateToTagManagerRequested(string folderPath)
        {
            _navigationService.NavigateTo(NavigationTarget.TagManager, folderPath);
        }

        // ── Cover-Verwaltung ─────────────────────────────────────────────────────

        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog, damit der Nutzer manuell ein Bild als
        /// Serien-Cover auswählen kann. Die Bytes werden dann ans ViewModel übergeben.
        /// Der FileOpenPicker benötigt das HWND des Hauptfensters.
        /// </summary>
        private async void OnSeriesCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                byte[]? bytes = await PickImageFileAsync();

                if (bytes is not null)
                {
                    await ViewModel.ApplySeriesCoverFromBytesAsync(card, bytes);
                }
            });
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Serie.
        /// Im Dialog kann der Suchbegriff angepasst und die Suche wiederholt werden,
        /// bevor ein Ergebnis übernommen wird.
        /// </summary>
        /// <summary>
        /// Schließt den Folgenbereich komplett – setzt die Serienauswahl zurück.
        /// Die Serien-Kacheln füllen wieder die volle Breite, kein Akkordeon sichtbar.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            ViewModel.DeselectArtist();
        }

        /// <summary>
        /// Tab-Wechsel: reguläre Folgen anzeigen.
        /// </summary>
        private void OnEpisodeTabRegularChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 0;
        }

        /// <summary>
        /// Tab-Wechsel: Sonderfolgen anzeigen.
        /// </summary>
        private void OnEpisodeTabSpecialChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 1;
        }

        /// <summary>
        /// Markiert eine Episode als gehört (nach Bestätigung).
        /// </summary>
        private async void OnEpisodeMarkPlayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkEpisodeAsPlayedAsync(episodeId));
        }

        /// <summary>
        /// Markiert eine Episode als ungehört (nach Bestätigung).
        /// </summary>
        private async void OnEpisodeMarkUnplayedClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkEpisodeAsUnplayedAsync(episodeId));
        }

        /// <summary>
        /// Startet den Ordnerstruktur-Assistenten für die gewählte Serie.
        /// Zeigt eine Vorschau der geplanten Verschiebungen und führt sie bei Bestätigung aus.
        /// </summary>
        private async void OnRestructureClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Vorschau im Hintergrund erstellen
                RestructurePreviewDisplay? preview = null;
                ViewModel.RestructurePreviewReady += p => preview = p;
                await ViewModel.AnalyzeRestructureAsync(seriesId);
                ViewModel.RestructurePreviewReady -= p => preview = p;

                if (preview is null || preview.IsEmpty)
                {
                    return;
                }

                using Helpers.DialogReentrancyGuard? restructureGuard = Helpers.DialogReentrancyGuard.TryAcquire();
                if (restructureGuard is null) return;

                // Vorschau-Text zusammenbauen
                System.Text.StringBuilder sb = new();
                _ = sb.Append(preview.FileCount).Append(" Dateien \u2192 ").Append(preview.FolderCount).AppendLine(" Ordner");
                _ = sb.AppendLine();

                foreach (RestructureActionDisplay action in preview.Actions)
                {
                    _ = sb.Append("  ").AppendLine(action.FileName);
                    _ = sb.Append("    \u2192 ").Append(action.TargetFolderName).AppendLine("/");
                }

                ContentDialogResult result = await Helpers.ScrollableTextDialog.ShowAsync(
                    Content.XamlRoot,
                    title: _resources.GetString("RestructureDialogTitle"),
                    content: sb.ToString(),
                    primaryButtonText: _resources.GetString("RestructureExecuteButton"),
                    closeButtonText: _resources.GetString("CommonCancelButton"),
                    useMonospace: true,
                    monospaceFontSize: 12,
                    defaultButton: ContentDialogButton.Close);

                if (result == ContentDialogResult.Primary)
                {
                    int movedCount = await ViewModel.ExecuteRestructureAsync(preview);

                    // Nach dem Umbau: Bibliothek neu laden, damit die neue Struktur sichtbar wird
                    if (movedCount > 0)
                    {
                        await ViewModel.LoadAsync();
                    }
                }
            });
        }

        private async void OnSeriesCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid seriesId })
            {
                return;
            }

            LocalArtistCardViewModel? card = ViewModel.Artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Offline-Modus: Nutzer fragen, bevor der Cover-Such-Dialog geöffnet wird
                using IDisposable? onlineScope = await ViewModel.RequestOnlineAccessForCoverSearchAsync();
                if (onlineScope is null) return;

                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, ct) => ViewModel.SearchCoversAsync(query, ct),
                    Content.XamlRoot);

                if (selected is not null)
                {
                    await ViewModel.ApplySelectedSeriesCoverAsync(card, selected);
                }
            });
        }

        /// <summary>
        /// Öffnet den Dateiauswahl-Dialog für ein Episoden-Cover.
        /// </summary>
        private async void OnEpisodeCoverSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            LocalEpisodeCardViewModel? card = ViewModel.Episodes.FirstOrDefault(ep => ep.EpisodeId == episodeId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                byte[]? bytes = await PickImageFileAsync();

                if (bytes is not null)
                {
                    await ViewModel.ApplyEpisodeCoverFromBytesAsync(card, bytes);
                }
            });
        }

        /// <summary>
        /// Öffnet sofort den Cover-Such-Dialog für die gewählte Episode.
        /// </summary>
        private async void OnEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId })
            {
                return;
            }

            LocalEpisodeCardViewModel? card = ViewModel.Episodes.FirstOrDefault(ep => ep.EpisodeId == episodeId);

            if (card is null)
            {
                return;
            }

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Offline-Modus: Nutzer fragen, bevor der Cover-Such-Dialog geöffnet wird
                using IDisposable? onlineScope = await ViewModel.RequestOnlineAccessForCoverSearchAsync();
                if (onlineScope is null) return;

                CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, ct) => ViewModel.SearchCoversAsync(query, ct),
                    Content.XamlRoot);

                if (selected is not null)
                {
                    await ViewModel.ApplySelectedEpisodeCoverAsync(card, selected);
                }
            });
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
