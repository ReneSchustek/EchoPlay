using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zentralisiert das WinUI3-spezifische Picker-Setup (Window-Handle, InitializeWithWindow)
    /// und entlässt die Pages aus Boilerplate-Code. Vor Arbeitspaket 293 hatten <c>PlayerPage</c>,
    /// <c>TagManagerPage</c> und <c>MediathekLokalPage</c> jeweils eigene Picker-Konstruktion
    /// mit identischer <c>WindowNative.GetWindowHandle</c> + <c>InitializeWithWindow.Initialize</c>-Sequenz.
    /// </summary>
    /// <remarks>
    /// Kein <c>CancellationToken</c> in den Signaturen: die WinRT-Picker nehmen keinen
    /// entgegen, und ein Dialog, den der Nutzer offen hat, wird nicht von außen
    /// abgebrochen — er wird geschlossen.
    /// </remarks>
    public interface IFilePickerService
    {
        /// <summary>Öffnet einen Folder-Picker. Liefert <c>null</c>, wenn der Nutzer abbricht.</summary>
        /// <param name="startLocation">Startort des Dialogs, z. B. die Musikbibliothek.</param>
        Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation = PickerLocationId.Unspecified);

        /// <summary>Öffnet einen File-Open-Picker mit Mehrfachauswahl.</summary>
        Task<IReadOnlyList<StorageFile>> PickFilesAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified);

        /// <summary>Öffnet einen File-Open-Picker für eine einzelne Datei. Liefert <c>null</c> bei Abbruch.</summary>
        Task<StorageFile?> PickFileAsync(
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified);

        /// <summary>Öffnet einen Save-File-Picker. Liefert <c>null</c> bei Abbruch.</summary>
        Task<StorageFile?> PickSaveFileAsync(
            string suggestedFileName,
            string fileTypeDescription,
            IReadOnlyList<string> fileTypeFilters,
            PickerLocationId startLocation = PickerLocationId.Unspecified);
    }
}
