using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Die Schriften des geladenen Themes für Stellen, die ihre Elemente im Code bauen und deshalb
    /// keine <c>ThemeResource</c>-Bindung haben.
    /// </summary>
    internal static class ThemeFonts
    {
        // Greift nur, wenn das Theme keine Mono-Schrift führt oder keine Anwendung läuft (Tests).
        private const string MonoFallback = "Consolas";

        /// <summary>Die Mono-Schrift des Themes für Muster, Berichte und Dateilisten.</summary>
        /// <returns>Die Schrift aus <c>ThemeMonoFontFamily</c>, sonst Consolas.</returns>
        public static FontFamily Mono()
        {
            try
            {
                // Der Indexer sucht auch in den Theme-Wörterbüchern der geladenen Palette.
                if (Application.Current?.Resources["ThemeMonoFontFamily"] is FontFamily font)
                {
                    return font;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Ohne WinUI-Runtime gibt es kein Theme; die Rückfallschrift genügt.
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                // Ein Theme ohne Mono-Schrift: ebenfalls die Rückfallschrift.
            }

            return new FontFamily(MonoFallback);
        }
    }
}
