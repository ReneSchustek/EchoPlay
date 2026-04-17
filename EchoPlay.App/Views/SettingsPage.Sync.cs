using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// RadioButton- und ComboBox-Synchronisation: Setzt beim Seitenaufruf die UI-Elemente
    /// auf den gespeicherten Zustand.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>
        /// Setzt die korrekte Theme-Kachel basierend auf dem gespeicherten Theme-Namen.
        /// Das Guard-Flag <see cref="_isSyncingThemeRadioButtons"/> verhindert, dass das
        /// ausgelöste SelectionChanged-Event einen erneuten Theme-Wechsel verursacht.
        /// </summary>
        private void SyncThemeRadioButton(string themeName)
        {
            _isSyncingThemeRadioButtons = true;
            try
            {
                foreach (object item in ThemeGridView.Items)
                {
                    if (item is ThemePreviewViewModel preview && preview.Tag == themeName)
                    {
                        ThemeGridView.SelectedItem = preview;
                        break;
                    }
                }
            }
            finally
            {
                _isSyncingThemeRadioButtons = false;
            }
        }

        /// <summary>
        /// Setzt den korrekten Provider-RadioButton basierend auf dem ViewModel-Tag-String.
        /// </summary>
        private void SyncProviderRadioButton(string providerTag)
        {
            switch (providerTag)
            {
                case "Spotify": RadioSpotify.IsChecked = true; break;
                case "AppleMusic": RadioAppleMusic.IsChecked = true; break;
                case "Both": RadioBoth.IsChecked = true; break;
                default: RadioNone.IsChecked = true; break;
            }
        }

        /// <summary>
        /// Setzt das ausgewählte Element der Sprach-ComboBox auf den gespeicherten Sprachcode.
        /// </summary>
        private void SyncLanguageComboBox(string languageCode)
        {
            foreach (LanguageOption option in ViewModel.AvailableLanguages)
            {
                if (option.Code == languageCode)
                {
                    LanguageComboBox.SelectedItem = option;
                    return;
                }
            }

            // Fallback auf erste Sprache wenn kein passender Eintrag gefunden wurde
            if (ViewModel.AvailableLanguages.Count > 0)
            {
                LanguageComboBox.SelectedItem = ViewModel.AvailableLanguages[0];
            }
        }
    }
}
