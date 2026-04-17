using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Reflection;
using Windows.Graphics;

namespace EchoPlay.App
{
    /// <summary>
    /// Startbildschirm der Anwendung.
    /// Wird beim App-Start angezeigt, während Datenbank und DI-Container initialisiert werden.
    /// Schließt sich erst, wenn <see cref="App.OnLaunched"/> das Hauptfenster aktiviert hat.
    /// </summary>
    public sealed partial class SplashWindow : Window
    {
        private const int FensterBreite = 480;
        private const int FensterHoehe = 300;

        /// <summary>
        /// Erstellt das Splash-Fenster, zeigt die Version an und positioniert es mittig auf dem Bildschirm.
        /// </summary>
        public SplashWindow()
        {
            this.InitializeComponent();

            ZeigeVersion();
            ZentriereUndGroesse();
            EntferneTitelleiste();
        }

        /// <summary>
        /// Liest die Assembly-Version und zeigt sie im Versionsfeld an.
        /// Fällt auf "1.0.0" zurück, wenn keine Versionsinformation vorhanden ist.
        /// </summary>
        private void ZeigeVersion()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = version is not null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.0.0";
        }

        /// <summary>
        /// Setzt die Fenstergröße und zentriert es auf dem primären Bildschirm.
        /// </summary>
        private void ZentriereUndGroesse()
        {
            AppWindow appWindow = this.AppWindow;
            appWindow.Resize(new SizeInt32(FensterBreite, FensterHoehe));

            DisplayArea displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
            int x = (displayArea.WorkArea.Width - FensterBreite) / 2;
            int y = (displayArea.WorkArea.Height - FensterHoehe) / 2;
            appWindow.Move(new PointInt32(x, y));
        }

        /// <summary>
        /// Aktualisiert den Statustext unterhalb des Ladebalkens.
        /// Wird von <see cref="App.OnLaunched"/> aufgerufen, um den aktuellen Startup-Schritt anzuzeigen.
        /// </summary>
        /// <param name="text">Der anzuzeigende Statustext.</param>
        public void SetStatus(string text)
        {
            StatusTextBlock.Text = text;
        }

        /// <summary>
        /// Blendet die Titelleiste aus – der Splash wirkt als reines Branding-Fenster ohne Fensterchrom.
        /// </summary>
        private void EntferneTitelleiste()
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
        }
    }
}
