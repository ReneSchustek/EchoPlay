using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Tag-Manager-Seite: Zeigt eine Dateiliste und einen Tag-Editor für Audiodateien.
    /// Der FolderPicker sowie der Online-Lookup-Dialog (MusicBrainz-Ergebnisse) werden
    /// hier im Code-Behind geöffnet, da sie WinRT-Interop oder ContentDialogs benötigen.
    /// </summary>
    public sealed partial class TagManagerPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public TagManagerViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public TagManagerPage()
        {
            ViewModel = App.Services.GetRequiredService<TagManagerViewModel>();
            InitializeComponent();
        }

        /// <summary>
        /// Abonniert das LookupResultsReady-Event des ViewModels beim Navigieren zur Seite.
        /// Das Event wird hier (nicht im ViewModel) verarbeitet, weil der Anzeige-Dialog WinUI-spezifisch ist.
        /// Wenn ein Ordnerpfad als Navigationsparameter übergeben wurde (z.B. von der lokalen Mediathek),
        /// wird der Ordner direkt geladen – der Nutzer muss ihn nicht manuell auswählen.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LookupResultsReady  += OnLookupResultsReady;
            ViewModel.AutoLookupApplied   += OnAutoLookupApplied;
            ViewModel.LoadCoverRequested  += OnLoadCoverRequested;
            ViewModel.RenamePreviewReady  += OnRenamePreviewReady;

            if (e.Parameter is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
            {
                await ViewModel.LoadFolderAsync(folderPath);
            }
        }

        /// <summary>
        /// Deabonniert Events beim Verlassen der Seite, um Memory-Leaks durch Event-Handler zu verhindern.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.LookupResultsReady  -= OnLookupResultsReady;
            ViewModel.AutoLookupApplied   -= OnAutoLookupApplied;
            ViewModel.LoadCoverRequested  -= OnLoadCoverRequested;
            ViewModel.RenamePreviewReady  -= OnRenamePreviewReady;
        }

        /// <summary>
        /// Öffnet einen Ordner-Auswahl-Dialog und lädt alle Audio-Dateien aus dem gewählten Ordner.
        /// WinUI 3 erfordert das Fenster-Handle für den FolderPicker via WinRT-Interop.
        /// </summary>
        private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            FolderPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode               = PickerViewMode.List
            };

            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            await ViewModel.LoadFolderAsync(folder.Path);
        }

        /// <summary>
        /// Zeigt die MusicBrainz-Suchergebnisse als ContentDialog an.
        /// Der Nutzer wählt ein Ergebnis aus und bestätigt mit "Übernehmen".
        /// Die gewählten Felder werden dann in den Tag-Editor übernommen.
        /// </summary>
        private async void OnLookupResultsReady(object? sender, IReadOnlyList<TagLookupCandidate> results)
        {
            Windows.ApplicationModel.Resources.ResourceLoader resources =
                Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();

            if (results.Count == 0)
            {
                ContentDialog noResultsDialog = new()
                {
                    Title              = resources.GetString("TagManagerNoResultsTitle"),
                    Content            = resources.GetString("TagManagerNoResultsMessage"),
                    CloseButtonText    = "OK",
                    XamlRoot           = XamlRoot
                };
                Helpers.ContentDialogDragHelper.MakeDraggable(noResultsDialog);
                await noResultsDialog.ShowAsync();
                return;
            }

            // ListView mit den Suchergebnissen als Dialog-Inhalt
            ListView resultList = new()
            {
                ItemsSource = results,
                Height      = 200
            };

            resultList.ItemTemplate = (DataTemplate)Resources["LookupResultTemplate"];

            ContentDialog dialog = new()
            {
                Title               = resources.GetString("TagManagerLookupDialogTitle"),
                Content             = resultList,
                PrimaryButtonText   = resources.GetString("TagManagerLookupApplyButton"),
                CloseButtonText     = resources.GetString("TagManagerLookupCancelButton"),
                XamlRoot            = XamlRoot
            };

            // "Übernehmen" nur aktivieren wenn ein Eintrag ausgewählt ist
            dialog.IsPrimaryButtonEnabled = false;
            resultList.SelectionChanged += (_, _) =>
            {
                dialog.IsPrimaryButtonEnabled = resultList.SelectedItem is not null;
            };

            Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult dialogResult = await dialog.ShowAsync();

            if (dialogResult == ContentDialogResult.Primary && resultList.SelectedItem is TagLookupCandidate selected)
            {
                ViewModel.ApplyLookupCandidate(selected.Index);
            }
        }

        /// <summary>
        /// Zeigt eine InfoBar-Bestätigung wenn der Auto-Lookup einen eindeutigen Treffer
        /// gefunden und die Tags automatisch übernommen hat.
        /// </summary>
        private void OnAutoLookupApplied(object? sender, TagLookupCandidate result)
        {
            // Kurze Bestätigung statt Dialog – die Tags sind bereits übernommen
            AutoLookupInfoBar.Message = $"Tags übernommen: {result.Artist} – {result.Album}";
            AutoLookupInfoBar.IsOpen  = true;
        }

        /// <summary>
        /// Verarbeitet die Auswahländerung in der Dateiliste.
        /// Übergibt die selektierten Dateien an das ViewModel für Einzel- oder Mehrfachbearbeitung.
        /// </summary>
        private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List<TagFileItemViewModel> selected = FileListView.SelectedItems
                .OfType<TagFileItemViewModel>()
                .ToList();

            ViewModel.SetSelectedFiles(selected);
        }

        /// <summary>
        /// Scrollt nach automatischer Rename-Vorschau zur Rename-Sektion,
        /// damit der Nutzer direkt die Vorschau sieht und umbenennen kann.
        /// </summary>
        private void OnRenamePreviewReady(object? sender, EventArgs e)
        {
            RenameSectionLabel.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        }

        /// <summary>
        /// Öffnet einen Datei-Picker für Bilddateien und übergibt das gewählte Bild an das ViewModel.
        /// WinUI 3 erfordert das Fenster-Handle für den FileOpenPicker via WinRT-Interop.
        /// </summary>
        private async void OnLoadCoverRequested(object? sender, EventArgs e)
        {
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode               = PickerViewMode.Thumbnail
            };

            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();

            if (file is null)
            {
                return;
            }

            // MIME-Typ aus Dateiendung ableiten
            string mimeType = file.FileType.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                _      => "image/jpeg"
            };

            byte[] imageData = await File.ReadAllBytesAsync(file.Path);
            ViewModel.SetCoverFromFile(imageData, mimeType);
        }
    }
}
