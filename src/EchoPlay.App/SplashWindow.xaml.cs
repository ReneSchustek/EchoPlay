using EchoPlay.Logger.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace EchoPlay.App
{
    /// <summary>
    /// Startbildschirm der Anwendung.
    /// Steht, während Datenbank und Dienst-Container eingerichtet werden, und schließt sich erst,
    /// wenn <see cref="App.OnLaunched"/> das Hauptfenster aktiviert hat.
    /// </summary>
    public sealed partial class SplashWindow : Window
    {
        // AppWindow.Resize rechnet in physischen Pixeln. Bei 100 % Skalierung fasst die Höhe die
        // Spalte aus dem XAML, die rund 280 Pixel braucht.
        private const int WindowWidth = 480;
        private const int WindowHeight = 300;

        // Per LogicalName in der csproj festgelegt; ein Build-Ziel dort bricht ab, wenn der
        // Eintrag fehlt. Ohne Logo bliebe der Startbildschirm trotzdem stehen.
        private const string LogoResourceName = "EchoPlay.App.Assets.logo.png";

        /// <summary>
        /// Erstellt das Splash-Fenster, zeigt die Version an und positioniert es mittig auf dem Bildschirm.
        /// </summary>
        public SplashWindow()
        {
            this.InitializeComponent();

            ShowVersion();
            CenterAndResize();
            HideTitleBar();

            // WCAG 1.1.1: Das Logo ist Schmuck; der Bildschirmleser überspringt es.
            // Im Code statt im XAML, weil die Angabe im Markup unter unpackaged WinUI 3 beim
            // Einlesen mit "Window.Title set more than once" scheitert.
            AutomationProperties.SetAccessibilityView(LogoImage, AccessibilityView.Raw);
        }

        /// <summary>
        /// Lädt das Logo aus der eingebetteten Ressource. Muss vor <see cref="Window.Activate"/>
        /// abgewartet werden, sonst zeigt der erste Frame den Startbildschirm kurz ohne Logo.
        /// Fehlt die Ressource oder ist sie kein Bild, bleibt das Logo leer; der Start geht weiter.
        /// </summary>
        public async Task LoadEmbeddedLogoAsync()
        {
            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResourceName);

            if (resourceStream is null)
            {
                EmergencyTrace.Log($"[SplashWindow] Eingebettete Ressource '{LogoResourceName}' fehlt, Startbildschirm ohne Logo.");
                return;
            }

            BitmapImage? bitmap = await DecodeStreamAsBitmapAsync(resourceStream).ConfigureAwait(true);
            if (bitmap is not null)
            {
                LogoImage.Source = bitmap;
            }
        }

        /// <summary>
        /// Aktualisiert den Statustext unterhalb des Ladebalkens.
        /// Die Prüfung beim Start meldet darüber, bei welchem Schritt sie steht.
        /// </summary>
        /// <param name="text">Der anzuzeigende Statustext.</param>
        public void SetStatus(string text)
        {
            StatusTextBlock.Text = text;
        }

        /// <summary>
        /// Liest die Assembly-Version und zeigt sie im Versionsfeld an.
        /// Fällt auf "1.0.0" zurück, wenn keine Versionsinformation vorhanden ist.
        /// </summary>
        private void ShowVersion()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = version is not null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.0.0";
        }

        /// <summary>
        /// Setzt die Fenstergröße und zentriert es auf dem primären Bildschirm.
        /// </summary>
        private void CenterAndResize()
        {
            AppWindow appWindow = this.AppWindow;
            appWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));

            DisplayArea displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
            int x = (displayArea.WorkArea.Width - WindowWidth) / 2;
            int y = (displayArea.WorkArea.Height - WindowHeight) / 2;
            appWindow.Move(new PointInt32(x, y));
        }

        /// <summary>
        /// Blendet die Titelleiste aus; der Startbildschirm zeigt nur Logo und Fortschritt.
        /// </summary>
        private void HideTitleBar()
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
        }

        /// <summary>
        /// Dekodiert beliebige <see cref="Stream"/>-Daten als <see cref="BitmapImage"/>.
        /// Liefert <c>null</c>, wenn die Daten kein gültiges Bild sind.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "BitmapImage.SetSourceAsync wirft bei korrupten Bilddaten native WIC/COM-Fehler ohne stabile Typ-Hierarchie; für den Splash reicht ein null-Rückgabewert plus EmergencyTrace, ohne den App-Start zu blockieren.")]
        private static async Task<BitmapImage?> DecodeStreamAsBitmapAsync(Stream source)
        {
            try
            {
                byte[] data = await ReadAllBytesAsync(source).ConfigureAwait(true);
                BitmapImage bitmap = new();
                using InMemoryRandomAccessStream randomStream = new();
                _ = await randomStream.WriteAsync(data.AsBuffer());
                randomStream.Seek(0);
                await bitmap.SetSourceAsync(randomStream);
                return bitmap;
            }
            catch (Exception ex)
            {
                EmergencyTrace.Log($"[SplashWindow] Bild-Dekodierung fehlgeschlagen: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Liest einen Stream vollständig in ein Byte-Array. Das Logo ist wenige Kilobyte groß;
        /// die Kopie löst die Dekodierung vom Quell-Stream, den der Aufrufer danach schließt.
        /// </summary>
        private static async Task<byte[]> ReadAllBytesAsync(Stream source)
        {
            using MemoryStream memory = new();
            await source.CopyToAsync(memory).ConfigureAwait(true);
            return memory.ToArray();
        }
    }
}
