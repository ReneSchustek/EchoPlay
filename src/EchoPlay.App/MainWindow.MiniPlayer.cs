using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Globalization;

namespace EchoPlay.App
{
    /// <summary>
    /// Der Mini-Player im Hauptfenster: Wiedergabe, Spulen, Lautstärke, Geschwindigkeit,
    /// Einschlaf-Timer und der Sprung zur Player-Seite.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>
        /// Schaltet zwischen Play und Pause um und aktualisiert das Icon.
        /// </summary>
        /// <param name="sender">Der Play/Pause-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (MiniPlayer.IsPlaying)
            {
                _playerService.Pause();
            }
            else
            {
                _playerService.Resume();
            }
        }

        /// <summary>
        /// Springt zu der vom Nutzer gewählten Position im Track.
        /// Programmatische Slider-Updates (via Timer) werden durch das Flag unterdrückt.
        /// </summary>
        /// <param name="sender">Der Seek-Slider.</param>
        /// <param name="e">Enthält den neuen Wert.</param>
        private void OnSeekSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Programmatische Updates durch OnMiniPlayerPropertyChanged ignorieren
            if (_isSeekingFromSlider)
            {
                return;
            }

            MiniPlayer.SeekTo(e.NewValue);
        }

        /// <summary>
        /// Schreibt die eingestellte Lautstärke in die Einstellungen, sobald der Nutzer den
        /// Regler loslässt. Während des Ziehens wirkt sie sofort, gespeichert wird aber erst
        /// am Ende, sonst löste jede Zwischenstellung einen Schreibvorgang aus.
        /// </summary>
        private async void OnVolumeSliderReleased(object sender, PointerRoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => MiniPlayer.Volume.CommitAsync());
        }

        /// <summary>
        /// Speichert die Lautstärke auch dann, wenn der Regler mit der Tastatur bedient
        /// wurde; dort gibt es kein Loslassen der Maus.
        /// </summary>
        private async void OnVolumeSliderLostFocus(object sender, RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => MiniPlayer.Volume.CommitAsync());
        }

        /// <summary>
        /// Aktualisiert das Play/Pause-Glyph und den Slider-Wert, wenn der PlayerService den Zustand wechselt.
        /// Der Slider wird per Code-Behind gesetzt, um die Rückkopplungsschleife zwischen
        /// PositionSeconds-Bindung und ValueChanged zu vermeiden.
        /// </summary>
        /// <param name="sender">Das MiniPlayerViewModel.</param>
        /// <param name="e">Enthält den Namen der geänderten Property.</param>
        private void OnMiniPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MiniPlayerViewModel.IsPlaying))
            {
                // Pause-Glyph (E769) wenn gespielt wird, Play-Glyph (E768) wenn pausiert
                PlayPauseIcon.Glyph = MiniPlayer.IsPlaying ? "\uE769" : "\uE768";
            }
            else if (e.PropertyName == nameof(MiniPlayerViewModel.PositionSeconds))
            {
                // Flag setzen, damit OnSeekSliderChanged den Seek nicht nochmals auslöst
                _isSeekingFromSlider = true;
                SeekSlider.Value = MiniPlayer.PositionSeconds;
                _isSeekingFromSlider = false;
            }
        }

        /// <summary>
        /// Setzt die Wiedergabegeschwindigkeit aus der ComboBox-Auswahl.
        /// </summary>
        /// <param name="sender">Die Geschwindigkeits-ComboBox.</param>
        /// <param name="e">Enthält das neu gewählte Element.</param>
        private void OnSpeedSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedComboBox.SelectedItem is ComboBoxItem item &&
                double.TryParse(
                    item.Tag?.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double rate))
            {
                MiniPlayer.PlaybackRate = rate;
            }
        }

        /// <summary>Aktiviert den Einschlaf-Timer für 15 Minuten.</summary>
        private void OnSleepTimer15Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(15));

        /// <summary>Aktiviert den Einschlaf-Timer für 30 Minuten.</summary>
        private void OnSleepTimer30Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(30));

        /// <summary>Aktiviert den Einschlaf-Timer für 45 Minuten.</summary>
        private void OnSleepTimer45Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(45));

        /// <summary>Aktiviert den Einschlaf-Timer für 60 Minuten.</summary>
        private void OnSleepTimer60Clicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(TimeSpan.FromMinutes(60));

        /// <summary>Deaktiviert den Einschlaf-Timer.</summary>
        private void OnSleepTimerOffClicked(object sender, RoutedEventArgs e) =>
            MiniPlayer.SetSleepTimer(null);

        /// <summary>
        /// Stoppt die Wiedergabe und blendet den MiniPlayer sofort aus. Gemeinsamer Weg der
        /// Stopp-Taste und des ✕.
        /// <see cref="PlayerService.Stop"/> verlagert die Media-Pipeline-Operationen
        /// intern auf einen Hintergrund-Thread, daher ist der Aufruf vom UI-Thread sicher.
        /// </summary>
        private void OnMiniPlayerStopClick(object sender, RoutedEventArgs e)
        {
            MiniPlayerPanel.Visibility = Visibility.Collapsed;
            MiniPlayer.StopCommand.Execute(null);
        }

        /// <summary>
        /// Führt vom Mini-Player auf die Player-Seite. Sie übernimmt die laufende Wiedergabe
        /// von sich aus, es genügt also die Navigation.
        /// Der Weg über <see cref="NavigationView.SelectedItem"/> statt direkt über den
        /// Navigationsdienst ist Absicht: So wandert die Markierung im Menü mit, und die
        /// Abfrage auf ungespeicherte Änderungen der Einstellungsseite greift weiterhin.
        /// Für Tastaturnutzer bleibt der Menüpunkt „Player" der Weg, er führt zum selben Ziel.
        /// </summary>
        private void OnMiniPlayerTapped(object sender, TappedRoutedEventArgs e)
        {
            // Die Bedienelemente des Mini-Players sollen bedienen, nicht navigieren.
            if (IsInsideInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (!ReferenceEquals(NavView.SelectedItem, NavPlayer))
            {
                NavView.SelectedItem = NavPlayer;
            }
        }

        /// <summary>
        /// Prüft, ob der Ursprung eines Klicks innerhalb eines Bedienelements des Mini-Players
        /// liegt. Die Suche endet am Panel selbst. Darüber hinaus interessiert sie nicht.
        /// </summary>
        /// <param name="source">Das Element, auf dem der Klick entstanden ist.</param>
        /// <returns><see langword="true"/>, wenn ein Bedienelement getroffen wurde.</returns>
        private bool IsInsideInteractiveElement(DependencyObject? source)
        {
            for (DependencyObject? current = source;
                 current is not null && !ReferenceEquals(current, MiniPlayerPanel);
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or Slider or ComboBox)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
