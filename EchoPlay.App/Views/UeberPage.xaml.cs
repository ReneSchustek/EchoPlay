using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
        }
    }
}
