using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Vollständiger Player mit Playlist-Verwaltung.
    /// Unterstützt das Öffnen von Ordnern (alle MP3-Dateien) und einzelner Dateien.
    /// Der FolderPicker/FileOpenPicker erfordert WinRT-Interop, da WinUI 3 kein Fenster-Handle
    /// automatisch bereitstellt – es muss explizit übergeben werden.
    /// </summary>
    public sealed partial class PlayerPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public PlayerViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public PlayerPage()
        {
            ViewModel = App.Services.GetRequiredService<PlayerViewModel>();
            InitializeComponent();
        }

        /// <summary>
        /// Öffnet einen FolderPicker und lädt alle MP3-Dateien aus dem gewählten Ordner.
        /// Der zuletzt gewählte Ordner wird in den AppSettings gespeichert.
        /// </summary>
        private async void OnOpenFolderClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            FolderPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode               = PickerViewMode.List
            };

            picker.FileTypeFilter.Add("*");

            // WinUI 3 ohne UWP: Der Picker braucht das Fenster-Handle für den Dialog
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            ViewModel.LoadFolder(folder.Path);
            _ = ViewModel.SaveLastOpenedFolderAsync(folder.Path);
        }

        /// <summary>
        /// Öffnet einen FileOpenPicker für Mehrfachauswahl von MP3-Dateien.
        /// Die Reihenfolge der Auswahl bestimmt die Playlist-Reihenfolge.
        /// </summary>
        private async void OnOpenFilesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode               = PickerViewMode.List
            };

            // Alle unterstützten Audioformate im Dateidialog anbieten
            foreach (string ext in EchoPlay.Core.AudioExtensions.Supported)
            {
                picker.FileTypeFilter.Add(ext);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();

            if (files.Count == 0)
            {
                return;
            }

            List<string> paths = new(files.Count);

            foreach (StorageFile file in files)
            {
                paths.Add(file.Path);
            }

            ViewModel.LoadFiles(paths);
        }

        /// <summary>
        /// Doppelklick auf einen Playlist-Eintrag startet die Wiedergabe ab diesem Track.
        /// </summary>
        private void OnPlaylistItemClick(object sender, ItemClickEventArgs args)
        {
            if (args.ClickedItem is PlaylistItemViewModel item)
            {
                ViewModel.PlayItem(item);
            }
        }

        /// <summary>
        /// Signalisiert dem ViewModel, dass ein manueller Seek beginnt.
        /// Verhindert, dass der Slider während des Ziehens durch PlayerService-Updates zurückspringt.
        /// </summary>
        private void OnSliderPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.BeginSeek();
        }

        /// <summary>
        /// Übergibt die neue Slider-Position an den PlayerService und beendet den Seek-Modus.
        /// </summary>
        private void OnSliderPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                ViewModel.PositionSeconds = slider.Value;
            }

            ViewModel.CommitSeek();
        }

        /// <summary>
        /// Wechselt die rechte Zeitanzeige zwischen verbleibender Zeit und Gesamtdauer.
        /// </summary>
        private void OnTimeDisplayTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.ToggleTimeDisplayCommand.Execute(null);
        }

        private void OnHelpClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => HelpTip.IsOpen = true;
    }
}
