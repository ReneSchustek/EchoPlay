using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Abgleich zwischen Geräten in den Einstellungen: Ordnerwahl, Ein und Aus, „Jetzt abgleichen“.
    /// Der Ablauf liegt im <see cref="ViewModels.ReplicationSettingsViewModel"/>.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>Wählt den gemeinsamen Ordner.</summary>
        /// <param name="sender">Die Schaltfläche.</param>
        /// <param name="e">Ereignisdaten.</param>
        private async void OnReplicationFolderClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.ReplicationVM is not { } replication)
                {
                    return;
                }

                StorageFolder? folder = await FilePicker.PickFolderAsync(PickerLocationId.ComputerFolder);
                if (folder is not null)
                {
                    await replication.SetFolderAsync(folder.Path);
                }
            });
        }

        /// <summary>
        /// Übernimmt den Schalter. Der Schalter zeigt den Zustand nur an (einseitige Bindung); ob er
        /// gilt, entscheidet das Ansichtsmodell, das ohne Ordner gar nicht einschaltet.
        /// </summary>
        /// <param name="sender">Der Schalter.</param>
        /// <param name="e">Ereignisdaten.</param>
        private async void OnReplicationToggled(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.ReplicationVM is { } replication && sender is ToggleSwitch toggle && toggle.IsOn != replication.IsEnabled)
                {
                    await replication.SetEnabledAsync(toggle.IsOn);
                }
            });
        }

        /// <summary>Gleicht sofort ab.</summary>
        /// <param name="sender">Die Schaltfläche.</param>
        /// <param name="e">Ereignisdaten.</param>
        private async void OnReplicationRunNowClick(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (ViewModel.ReplicationVM is { } replication)
                {
                    await replication.RunNowAsync();
                }
            });
        }
    }
}
