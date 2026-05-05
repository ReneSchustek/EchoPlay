using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Fehlende-Folgen-Dialoge: Modus-Auswahl, Einzel- und Gesamtbericht,
    /// TXT-Export über den FileSavePicker.
    /// </summary>
    public sealed partial class MediathekLokalPage : Page
    {
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
                XamlRoot = XamlRoot,
                Title = _resources.GetString("MissingEpisodesDialogTitle"),
                Content = _resources.GetString("MissingEpisodesDialogContent"),
                PrimaryButtonText = _resources.GetString("MissingEpisodesOnlineButton"),
                SecondaryButtonText = _resources.GetString("MissingEpisodesOfflineButton"),
                CloseButtonText = _resources.GetString("CommonCancelButton")
            };

            try
            {
                Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
                ContentDialogResult result = await dialog.ShowAsync();

                return result switch
                {
                    ContentDialogResult.Primary => MissingEpisodesMode.WithOnline,
                    ContentDialogResult.Secondary => MissingEpisodesMode.OfflineOnly,
                    _ => MissingEpisodesMode.Cancel
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
            ResourceLoader resources = ResourceLoader.GetForViewIndependentUse();
            EchoPlay.App.Services.IFilePickerService picker =
                App.Services.GetRequiredService<EchoPlay.App.Services.IFilePickerService>();

            // Lokalzeit für den Datei-Namen — der Anwender erkennt seinen heutigen Bericht.
            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync(
                suggestedFileName: $"Fehlende-Folgen-{DateTime.Now:yyyy-MM-dd}",
                fileTypeDescription: resources.GetString("CommonTextFileFilter"),
                fileTypeFilters: [".txt"],
                startLocation: Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary);

            if (file is not null)
            {
                await Windows.Storage.FileIO.WriteTextAsync(file, reportText);
            }
        }
    }
}
