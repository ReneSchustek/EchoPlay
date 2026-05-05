using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EchoPlay.App.Services
{
    /// <inheritdoc/>
    public sealed class FilePickerService : IFilePickerService
    {
        // Window-Handle wird zur Aufrufzeit aus App.MainWindow gezogen, nicht im Konstruktor —
        // beim App-Start ist das Fenster noch nicht aktiviert, und ein Neustart kann die
        // MainWindow-Referenz wechseln.
        private static Window MainWindow =>
            App.MainWindow ?? throw new InvalidOperationException(
                "FilePickerService: MainWindow ist nicht gesetzt — Picker kann ohne aktiviertes Fenster nicht laufen.");

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="startLocation">Parameter <c>startLocation</c>.</param>
        public async Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default)
        {
            FolderPicker picker = new()
            {
                SuggestedStartLocation = startLocation,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));

            return await picker.PickSingleFolderAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<StorageFile>> PickFilesAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fileTypeFilters);

            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = startLocation,
                ViewMode = PickerViewMode.List
            };
            foreach (string ext in fileTypeFilters)
            {
                picker.FileTypeFilter.Add(ext);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));

            return await picker.PickMultipleFilesAsync();
        }

        /// <inheritdoc/>
        public async Task<StorageFile?> PickFileAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fileTypeFilters);

            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = startLocation,
                ViewMode = PickerViewMode.List
            };
            foreach (string ext in fileTypeFilters)
            {
                picker.FileTypeFilter.Add(ext);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));

            return await picker.PickSingleFileAsync();
        }

        /// <inheritdoc/>
        public async Task<StorageFile?> PickSaveFileAsync(
            string suggestedFileName,
            string fileTypeDescription,
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fileTypeFilters);

            FileSavePicker picker = new()
            {
                SuggestedStartLocation = startLocation,
                SuggestedFileName = suggestedFileName
            };
            picker.FileTypeChoices.Add(fileTypeDescription, [.. fileTypeFilters]);

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));

            return await picker.PickSaveFileAsync();
        }
    }
}
