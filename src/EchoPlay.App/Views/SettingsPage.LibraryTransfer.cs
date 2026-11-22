using System.Globalization;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Replication.Packaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Mediathek packen und auspacken in den Einstellungen: Ordner- und Dateiwahl und die Dialoge.
    /// Der Ablauf selbst liegt im <see cref="ViewModels.DataTransferViewModel"/>.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>Wählt den Zielordner, fragt nach dem Kennwort und packt die Mediathek.</summary>
        /// <param name="sender">Die Schaltfläche.</param>
        /// <param name="e">Ereignisdaten.</param>
        private async void OnLibraryPackClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.DataTransferVM is not { } transfer)
                {
                    return;
                }

                StorageFolder? folder = await FilePicker.PickFolderAsync(PickerLocationId.DocumentsLibrary);
                if (folder is null)
                {
                    return;
                }

                (bool proceed, string? password) = await AskExportPasswordAsync(await transfer.HasCredentialsAsync(), ExportDialogTexts.Library);
                if (proceed)
                {
                    await transfer.PackLibraryAsync(folder.Path, password);
                }
            });
        }

        /// <summary>
        /// Wählt Archiv und Zielordner, packt aus und lässt das Einspielen bestätigen. Wer nicht
        /// bestätigt, behält die Dateien; eingespielt wird nichts.
        /// </summary>
        /// <param name="sender">Die Schaltfläche.</param>
        /// <param name="e">Ereignisdaten.</param>
        private async void OnLibraryUnpackClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.DataTransferVM is not { } transfer)
                {
                    return;
                }

                StorageFile? archive = await FilePicker.PickFileAsync(new[] { LibraryArchiveFormat.FileExtension }, PickerLocationId.DocumentsLibrary);
                if (archive is null)
                {
                    return;
                }

                StorageFolder? target = await FilePicker.PickFolderAsync(PickerLocationId.ComputerFolder);
                if (target is null)
                {
                    return;
                }

                if (await transfer.UnpackLibraryAsync(archive.Path, target.Path) is { } unpacked)
                {
                    await ConfirmUnpackedImportAsync(transfer, unpacked);
                }
            });
        }

        /// <summary>
        /// Fragt nach dem Einspielen, bis es geplant ist oder der Nutzer ablehnt; ein falsches
        /// Kennwort führt zurück in die Rückfrage.
        /// </summary>
        private async Task ConfirmUnpackedImportAsync(ViewModels.DataTransferViewModel transfer, UnpackedLibrary unpacked)
        {
            string note = string.Format(CultureInfo.CurrentCulture, _resources.GetString("LibraryUnpackDialogNote"), unpacked.Files, unpacked.LibraryRoot);
            while (true)
            {
                (bool confirmed, string? password) = await ConfirmImportAsync(unpacked.Staged, note);
                if (!confirmed)
                {
                    transfer.DiscardUnpacked(unpacked);
                    return;
                }

                if (transfer.ScheduleAndRestart(unpacked.Staged, password, unpacked.LibraryRoot))
                {
                    return;
                }
            }
        }

        /// <summary>Die Texte des Kennwortdialogs: für den Datenexport oder für die Mediathek.</summary>
        /// <param name="TitleKey">Schlüssel des Titels.</param>
        /// <param name="TextKey">Schlüssel der Erklärung.</param>
        /// <param name="ConfirmKey">Schlüssel der Bestätigung.</param>
        private sealed record ExportDialogTexts(string TitleKey, string TextKey, string ConfirmKey)
        {
            /// <summary>Daten exportieren.</summary>
            public static ExportDialogTexts Data { get; } = new("DataExportDialogTitle", "DataExportDialogText", "DataExportConfirm");

            /// <summary>Mediathek packen.</summary>
            public static ExportDialogTexts Library { get; } = new("LibraryPackDialogTitle", "LibraryPackDialogText", "LibraryPackConfirm");
        }
    }
}
