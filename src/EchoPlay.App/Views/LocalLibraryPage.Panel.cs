using EchoPlay.App.Infrastructure;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Folgenbereich und Ordnerwahl der lokalen Mediathek: Auswahl und Start einer Folge, Wechsel
    /// zwischen regulären Folgen und Sonderfolgen, Schließen des Bereichs und die beiden
    /// Ordnerdialoge.
    /// </summary>
    public sealed partial class LocalLibraryPage : Page
    {
        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer eine Folgen-Kachel auswählt.
        /// Lädt die Tracks der gewählten Folge.
        /// </summary>
        private async void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is GridView { SelectedItem: LocalEpisodeCardViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.SelectEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Startet die Folge, auf die der Nutzer doppelt geklickt hat.
        /// Der erste Klick des Doppelklicks hat sie bereits ausgewählt und damit ihre Spuren
        /// geladen. Gespielt wird trotzdem über die Folgen-Id, damit der Doppelklick auch
        /// dann greift, wenn die Liste rechts noch lädt.
        /// </summary>
        private async void OnEpisodeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is GridView { SelectedItem: LocalEpisodeCardViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.TracksVM.PlayEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Öffnet den Ordnerpicker und speichert den gewählten Bibliothekspfad.
        /// Das HWND muss manuell aus dem Hauptfenster abgefragt werden, da WinRT-Picker
        /// keinen direkten Window-Zugriff haben.
        /// </summary>
        private async void OnPickFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.PickFolderAsync(handle));
        }

        /// <summary>
        /// Öffnet den Ordnerpicker, damit der Nutzer direkt einen Serienordner hinzufügen kann.
        /// </summary>
        private async void OnAddFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.AddFolderAsync(handle));
        }

        /// <summary>
        /// Reagiert auf das <see cref="LocalLibraryScanViewModel.AddFolderRequested"/>-Event.
        /// Das ViewModel selbst kennt das HWND nicht, die Page liefert es.
        /// </summary>
        private async void OnAddFolderRequested()
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.AddFolderAsync(handle));
        }

        /// <summary>
        /// Schließt den Folgenbereich komplett und setzt die Serienauswahl zurück.
        /// Die Serien-Kacheln füllen wieder die volle Breite, kein Akkordeon sichtbar.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            // Vor dem Zuklappen merken, wohin zurückgekehrt wird: Der Folgenbereich kann weit
            // unten aufgehen, und ohne Rücksprung stünde man danach irgendwo in der Liste statt
            // bei der Serie, die man gerade offen hatte.
            LocalArtistCardViewModel? lastOpened = ViewModel.ArtistsVM.SelectedArtist;

            ViewModel.DeselectArtist();

            if (lastOpened is null)
            {
                return;
            }

            // Erst nach dem Neuaufbau der Abschnitte scrollen, denn vorher gibt es den Container
            // der Kachel noch nicht, und der Sprung liefe ins Leere.
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _sectionHandler.BringItemIntoView(lastOpened));
        }

        /// <summary>Tab-Wechsel: reguläre Folgen anzeigen.</summary>
        private void OnEpisodeTabRegularChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodesVM.EpisodeTabIndex = 0;
        }

        /// <summary>Tab-Wechsel: Sonderfolgen anzeigen.</summary>
        private void OnEpisodeTabSpecialChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodesVM.EpisodeTabIndex = 1;
        }
    }
}
