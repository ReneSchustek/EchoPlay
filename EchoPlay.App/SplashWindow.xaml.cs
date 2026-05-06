using EchoPlay.Logger.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    /// Wird beim App-Start angezeigt, während Datenbank und DI-Container initialisiert werden.
    /// Schließt sich erst, wenn <see cref="App.OnLaunched"/> das Hauptfenster aktiviert hat.
    /// </summary>
    public sealed partial class SplashWindow : Window
    {
        private const int FensterBreite = 480;
        private const int FensterHoehe = 300;

        // Embedded-Resource-Name: <RootNamespace>.<Pfad-mit-Punkten>.<Datei>.
        // Per LogicalName in der csproj fixiert, damit MSBuild-Heuristiken den Namen nicht ändern.
        private const string LogoResourceName = "EchoPlay.App.Assets.logo.png";

        // Fallback-Pfad gemäß CLAUDE.md / splashscreen.md — greift nur, wenn die Embedded-Resource
        // fehlt. In Produktion existiert der Pfad nicht; der Splash zeigt dann kein Logo, statt zu crashen.
        private const string FallbackLogoPath = @"F:\Entwicklung\_Icons\Icon_Ruhrcoder_quadrat.png";

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
        /// Lädt das Splash-Logo aus der Embedded-Resource und setzt es als <see cref="BitmapImage"/>
        /// auf <c>LogoImage</c>. Muss vor <see cref="Window.Activate"/> awaited werden, damit das Logo
        /// bereits im ersten Frame sichtbar ist (sonst flackert der Splash kurz ohne Logo).
        /// </summary>
        public async Task LoadEmbeddedLogoAsync()
        {
            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResourceName);

            if (resourceStream is null)
            {
                EmergencyTrace.Log($"[SplashWindow] Embedded-Resource '{LogoResourceName}' nicht gefunden — versuche Fallback.");
                await LoadFallbackLogoAsync().ConfigureAwait(true);
                return;
            }

            BitmapImage? bitmap = await DecodeStreamAsBitmapAsync(resourceStream).ConfigureAwait(true);
            if (bitmap is null)
            {
                EmergencyTrace.Log($"[SplashWindow] Embedded-Logo konnte nicht dekodiert werden — versuche Fallback.");
                await LoadFallbackLogoAsync().ConfigureAwait(true);
                return;
            }

            LogoImage.Source = bitmap;
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

        /// <summary>
        /// Versucht den Dev-Maschinen-Fallback aus <see cref="FallbackLogoPath"/> zu laden.
        /// Schlägt das fehl (z. B. in Produktion, wo der Pfad nicht existiert), bleibt der Splash
        /// ohne Logo — bewusst, damit der Start nicht an einem Branding-Asset hängt.
        /// </summary>
        private async Task LoadFallbackLogoAsync()
        {
            if (!File.Exists(FallbackLogoPath))
            {
                EmergencyTrace.Log($"[SplashWindow] Fallback-Logo '{FallbackLogoPath}' nicht vorhanden — Splash bleibt logo-los.");
                return;
            }

            try
            {
                using FileStream fileStream = File.OpenRead(FallbackLogoPath);
                BitmapImage? bitmap = await DecodeStreamAsBitmapAsync(fileStream).ConfigureAwait(true);
                if (bitmap is not null)
                {
                    LogoImage.Source = bitmap;
                }
            }
            catch (IOException ex)
            {
                EmergencyTrace.Log($"[SplashWindow] Fallback-Logo IO-Fehler: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                EmergencyTrace.Log($"[SplashWindow] Fallback-Logo Zugriffsfehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Dekodiert beliebige <see cref="Stream"/>-Daten als <see cref="BitmapImage"/>.
        /// Liefert <c>null</c>, wenn die Daten kein gültiges Bild sind — der Aufrufer entscheidet,
        /// ob er einen Fallback versucht oder das Logo weglässt.
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
        /// Liest einen Stream vollständig in ein Byte-Array. Splash-Logos sind klein (wenige KB);
        /// die Komplettkopie vermeidet Lifecycle-Probleme zwischen Quell-Stream und der asynchronen
        /// Bild-Dekodierung.
        /// </summary>
        private static async Task<byte[]> ReadAllBytesAsync(Stream source)
        {
            using MemoryStream memory = new();
            await source.CopyToAsync(memory).ConfigureAwait(true);
            return memory.ToArray();
        }
    }
}
