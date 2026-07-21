using EchoPlay.App.Helpers;
using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Über-Seite der Anwendung.
    /// Zeigt App-Logo, Version, Beschreibung und Autorenangabe.
    /// Ersetzt den früheren ContentDialog – die Seite ist eine normale NavigationView-Seite
    /// ohne eigenen Schließen-Button. Navigieren geht über den Zurück-Pfeil des NavigationView.
    /// </summary>
    public sealed partial class UeberPage : Page
    {
        /// <summary>
        /// Initialisiert die Seite.
        /// </summary>
        public UeberPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Liest die Assembly-Version und zeigt sie im VersionText-Block an.
        /// Die Version wird direkt aus den Assembly-Metadaten gelesen, damit sie nicht
        /// an zwei Stellen gepflegt werden muss.
        /// </summary>
        /// <param name="e">Navigationsparameter (werden nicht verwendet).</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // ToString(3) liefert "Major.Minor.Build" – die Build-Revision lassen wir weg
            string version = typeof(App).Assembly
                .GetName()
                .Version?
                .ToString(3) ?? "1.0.0";

            VersionText.Text = $"Version {version}";

            // Spenden-Einstieg nur zeigen, wenn ein echter PayPal-Handle hinterlegt ist –
            // sonst bliebe ein toter Platzhalter-Link stehen.
            SupportPanel.Visibility = SupportDonation.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Öffnet das fest verdrahtete PayPal-Spendenziel im System-Browser.
        /// Der <see cref="SafeUrlLauncher"/> lässt nur http/https zu; die URL selbst ist eine
        /// Compile-Zeit-Konstante (<see cref="SupportDonation.PayPalUrl"/>) und nicht manipulierbar.
        /// </summary>
        private void OnSupportClick(object sender, RoutedEventArgs e)
        {
            _ = SafeUrlLauncher.TryOpenInBrowser(SupportDonation.PayPalUrl);
        }

        /// <summary>
        /// Stößt eine manuelle Update-Prüfung an. Der Button ist während der Prüfung deaktiviert,
        /// damit kein Doppelklick zwei parallele Prüfungen startet. Fehler behandelt der
        /// <see cref="UpdateInteractionService"/> selbst (Hinweis-Dialog), der Aufruf wirft nie.
        /// </summary>
        private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            try
            {
                UpdateInteractionService service = App.Services.GetRequiredService<UpdateInteractionService>();
                await service.CheckForUpdatesAsync(XamlRoot);
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }
    }
}
