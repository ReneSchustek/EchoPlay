using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zentralisiert das WinUI3-spezifische Picker-Setup (Window-Handle, InitializeWithWindow)
    /// und entlaesst die Pages aus Boilerplate-Code. Pre-Brief-293 hatten <c>PlayerPage</c>,
    /// <c>TagManagerPage</c> und <c>MediathekLokalPage</c> jeweils eigene Picker-Konstruktion
    /// mit identischer <c>WindowNative.GetWindowHandle</c> + <c>InitializeWithWindow.Initialize</c>-Sequenz.
    /// </summary>

    public interface IFilePickerService
    {
        /// <summary>Oeffnet einen Folder-Picker. Liefert <c>null</c>, wenn der Nutzer abbricht.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="startLocation">Parameter <c>startLocation</c>.</param>
        Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default);

        /// <summary>Oeffnet einen File-Open-Picker mit Mehrfachauswahl.</summary>
        Task<IReadOnlyList<StorageFile>> PickFilesAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default);

        /// <summary>Oeffnet einen File-Open-Picker fuer eine einzelne Datei. Liefert <c>null</c> bei Abbruch.</summary>
        Task<StorageFile?> PickFileAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default);

        /// <summary>Oeffnet einen Save-File-Picker. Liefert <c>null</c> bei Abbruch.</summary>
        Task<StorageFile?> PickSaveFileAsync(
            string suggestedFileName,
            string fileTypeDescription,
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified, CancellationToken cancellationToken = default);
    }
}
