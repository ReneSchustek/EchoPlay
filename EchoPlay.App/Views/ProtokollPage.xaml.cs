using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Zeigt das Live-Protokoll der Anwendung an.
    /// Neue Log-Einträge erscheinen in Echtzeit, sobald sie anfallen – kein manuelles Nachladen.
    ///
    /// Die Seite verwaltet das Event-Abonnement im ViewModel:
    /// <see cref="OnNavigatedTo"/> aktiviert das Live-Update,
    /// <see cref="OnNavigatedFrom"/> hebt das Abonnement auf.
    /// </summary>
    public sealed partial class ProtokollPage : Page
    {
        /// <summary>ViewModel – von XAML über x:Bind referenziert.</summary>
        public ProtokollViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public ProtokollPage()
        {
            ViewModel = App.Services.GetRequiredService<ProtokollViewModel>();
            InitializeComponent();
        }

        /// <summary>
        /// Aktiviert das Live-Update beim Betreten der Seite.
        /// Der DispatcherQueue des UI-Threads wird übergeben, damit das ViewModel
        /// eingehende Log-Einträge sicher auf dem UI-Thread einfügen kann.
        /// </summary>
        /// <param name="e">Navigationsparameter (nicht verwendet).</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.Activate(DispatcherQueue);
        }

        /// <summary>
        /// Deaktiviert das Live-Update beim Verlassen der Seite.
        /// Verhindert, dass das ViewModel nach der Navigation noch UI-Updates auslöst.
        /// </summary>
        /// <param name="e">Navigationsparameter (nicht verwendet).</param>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.Deactivate();
        }

        /// <summary>
        /// Leitet den ToggleSwitch-Zustand an das ViewModel weiter.
        /// Der ToggleSwitch-Event wird hier verarbeitet statt über einen Command,
        /// weil <see cref="ToggleSwitch.Toggled"/> keinen einfachen ICommand-Binding-Support hat.
        /// </summary>
        /// <param name="sender">Der ToggleSwitch.</param>
        /// <param name="e">Ereignisargumente (nicht verwendet).</param>
        private void OnLiveToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.ToggleLiveCommand.Execute(null);
        }
    }
}
