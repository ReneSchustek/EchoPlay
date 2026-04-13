using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Wiederverwendbarer Datei-Picker für Bilddateien (JPG, JPEG, PNG).
    /// Kapselt das WinRT-Interop für den <see cref="FileOpenPicker"/>, das in WinUI 3
    /// das HWND des Hauptfensters erfordert. Zwei Varianten: nur Bytes (für einfache
    /// Cover-Auswahl) oder Bytes plus MIME-Typ (für den Tag-Manager, der den Typ
    /// als ID3-Metadatum speichert).
    /// </summary>
    internal static class ImageFilePicker
    {
        /// <summary>
        /// Öffnet den Datei-Picker und liest die Bytes der gewählten Bilddatei.
        /// Gibt <see langword="null"/> zurück, wenn der Nutzer abbricht.
        /// </summary>
        /// <param name="windowHandle">HWND des Hauptfensters – Pflicht für WinUI 3 Picker.</param>
        public static async Task<byte[]?> PickAsync(nint windowHandle)
        {
            StorageFile? file = await ShowPickerAsync(windowHandle);
            if (file is null)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(file.Path);
        }

        /// <summary>
        /// Öffnet den Datei-Picker und liest Bytes plus den abgeleiteten MIME-Typ
        /// (image/jpeg oder image/png). Wird vom Tag-Manager genutzt, der den Typ
        /// als ID3-Metadatum benötigt.
        /// </summary>
        public static async Task<(byte[] Data, string MimeType)?> PickWithMimeTypeAsync(nint windowHandle)
        {
            StorageFile? file = await ShowPickerAsync(windowHandle);
            if (file is null)
            {
                return null;
            }

            string mimeType = file.FileType.ToUpperInvariant() switch
            {
                ".PNG" => "image/png",
                _      => "image/jpeg"
            };

            byte[] data = await File.ReadAllBytesAsync(file.Path);
            return (data, mimeType);
        }

        /// <summary>
        /// Erstellt den FileOpenPicker mit den drei Standard-Bildformaten und übergibt
        /// das HWND, damit der Picker korrekt im Hauptfenster geankert wird.
        /// </summary>
        private static Task<StorageFile?> ShowPickerAsync(nint windowHandle)
        {
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode               = PickerViewMode.Thumbnail
            };

            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            InitializeWithWindow.Initialize(picker, windowHandle);

            return picker.PickSingleFileAsync().AsTask()!;
        }
    }
}
