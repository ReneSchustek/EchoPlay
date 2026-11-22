using System.Globalization;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Export und Einspielen im Wartung-Tab: Ziel- und Dateiwahl, Kennwort- und Bestätigungsdialog.
    /// </summary>
    /// <remarks>
    /// Das Ersetzen des Bestands lässt sich nicht ausblenden: Der Bestätigungsdialog hat kein
    /// Häkchen „nicht mehr fragen“. Das Kennwort für die Zugangsdaten muss beim Export zweimal
    /// gleich und mindestens acht Zeichen lang sein, sonst bleibt der Knopf gesperrt.
    /// </remarks>
    public sealed partial class SettingsPage : Page
    {
        private const int MinPasswordLength = 8;

        private static IFilePickerService FilePicker => App.Services.GetRequiredService<IFilePickerService>();

        private async void OnDataExportClick(object sender, RoutedEventArgs e)
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

                (bool proceed, string? password) = await AskExportPasswordAsync(await transfer.HasCredentialsAsync(), ExportDialogTexts.Data);
                if (proceed)
                {
                    await transfer.ExportAsync(folder.Path, password);
                }
            });
        }

        private async void OnDataImportClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.DataTransferVM is not { } transfer)
                {
                    return;
                }

                StorageFile? file = await FilePicker.PickFileAsync([PackageFormat.FileExtension], PickerLocationId.DocumentsLibrary);
                if (file is null)
                {
                    return;
                }

                if (await transfer.StageAsync(file.Path) is { } staged)
                {
                    await ConfirmStagedImportAsync(transfer, staged);
                }
            });
        }

        /// <summary>
        /// Fragt nach dem Einspielen, bis es geplant ist oder der Nutzer ablehnt.
        /// </summary>
        private async Task ConfirmStagedImportAsync(ViewModels.DataTransferViewModel transfer, StagedPackage staged)
        {
            while (true)
            {
                (bool confirmed, string? password) = await ConfirmImportAsync(staged);
                if (!confirmed)
                {
                    transfer.Discard();
                    return;
                }

                // Ein falsches Kennwort lässt den Dialog erneut erscheinen; die Meldung steht
                // unter den Knöpfen, und ohne Kennwort geht es ohne Zugangsdaten weiter.
                if (transfer.ScheduleAndRestart(staged, password))
                {
                    return;
                }
            }
        }

        private void OnDataTransferCancelClick(object sender, RoutedEventArgs e) => ViewModel.DataTransferVM?.Cancel();

        private async Task<(bool Proceed, string? Password)> AskExportPasswordAsync(bool hasCredentials, ExportDialogTexts texts)
        {
            CheckBox include = new()
            {
                Content = _resources.GetString("DataExportIncludeCredentials"),
                IsEnabled = hasCredentials,
            };
            PasswordBox first = new() { Header = _resources.GetString("DataExportPassword"), IsEnabled = false };
            PasswordBox second = new() { Header = _resources.GetString("DataExportPasswordRepeat"), IsEnabled = false };
            TextBlock hint = Caption(_resources.GetString(hasCredentials ? "DataExportPasswordHint" : "DataExportNoCredentials"));

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = _resources.GetString(texts.TitleKey),
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children = { Wrapped(_resources.GetString(texts.TextKey)), include, first, second, hint },
                },
                PrimaryButtonText = _resources.GetString(texts.ConfirmKey),
                CloseButtonText = _resources.GetString("CommonCancelButton"),
                DefaultButton = ContentDialogButton.Primary,
            };

            WireExportPasswordValidation(dialog, include, first, second);

            Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return (false, null);
            }

            return (true, include.IsChecked == true ? first.Password : null);
        }

        /// <summary>
        /// Schaltet die Kennwortfelder mit dem Haken frei und gibt die Bestätigung erst bei zwei
        /// gleichen, ausreichend langen Kennwörtern frei.
        /// </summary>
        private static void WireExportPasswordValidation(
            ContentDialog dialog,
            CheckBox include,
            PasswordBox first,
            PasswordBox second)
        {
            void Validate()
            {
                bool withCredentials = include.IsChecked == true;
                first.IsEnabled = withCredentials;
                second.IsEnabled = withCredentials;
                dialog.IsPrimaryButtonEnabled = !withCredentials
                    || (first.Password.Length >= MinPasswordLength && first.Password == second.Password);
            }

            include.Checked += (_, _) => Validate();
            include.Unchecked += (_, _) => Validate();
            first.PasswordChanged += (_, _) => Validate();
            second.PasswordChanged += (_, _) => Validate();
            Validate();
        }

        private async Task<(bool Confirmed, string? Password)> ConfirmImportAsync(StagedPackage staged, string? libraryNote = null)
        {
            (StackPanel content, PasswordBox? password) = BuildImportDialogContent(staged, libraryNote);

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = _resources.GetString("DataImportDialogTitle"),
                Content = content,
                PrimaryButtonText = _resources.GetString("DataImportConfirm"),
                CloseButtonText = _resources.GetString("CommonCancelButton"),
                DefaultButton = ContentDialogButton.Close,
            };

            Helpers.ContentDialogDragHelper.MakeDraggable(dialog);
            bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            string? entered = password?.Password;
            return (confirmed, string.IsNullOrEmpty(entered) ? null : entered);
        }

        /// <summary>
        /// Baut den Inhalt der Import-Rückfrage: Vergleich Paket und Bestand, optionaler Hinweis
        /// zur Mediathek und, wenn das Paket Zugangsdaten trägt, das Kennwortfeld.
        /// </summary>
        private (StackPanel Content, PasswordBox? Password) BuildImportDialogContent(StagedPackage staged, string? libraryNote)
        {
            StackPanel content = new() { Spacing = 12, Children = { Wrapped(FormatImportSummary(staged)) } };
            if (libraryNote is not null)
            {
                content.Children.Add(Wrapped(libraryNote));
            }

            PasswordBox? password = null;
            if (!staged.SealedCredentials.IsEmpty)
            {
                password = new PasswordBox { Header = _resources.GetString("DataImportPassword") };
                content.Children.Add(password);
                content.Children.Add(Caption(_resources.GetString("DataImportPasswordHint")));
            }

            if (ViewModel.DataTransferVM is { StatusText.Length: > 0 } transfer)
            {
                content.Children.Add(Caption(transfer.StatusText));
            }

            return (content, password);
        }

        private string FormatImportSummary(StagedPackage staged)
        {
            ImportPreview preview = staged.Preview;
            return string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("DataImportDialogText"),
                staged.Manifest.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                staged.Manifest.AppVersion,
                preview.PackageSeries, preview.PackageEpisodes, preview.PackageCompleted,
                preview.CurrentSeries, preview.CurrentEpisodes, preview.CurrentCompleted);
        }

        private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

        private static TextBlock Caption(string text) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
    }
}
