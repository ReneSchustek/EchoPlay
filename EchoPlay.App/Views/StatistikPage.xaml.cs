using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Statistik-Seite: zeigt Kennzahlen zur Sammlung und zum Hörfortschritt.
    /// </summary>
    public sealed partial class StatistikPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public StatistikViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public StatistikPage()
        {
            ViewModel = App.Services.GetRequiredService<StatistikViewModel>();
            InitializeComponent();
        }

        /// <summary>
        /// Lädt die Statistiken beim Navigieren zur Seite.
        /// </summary>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadAsync();
        }

        private void OnHelpClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => HelpTip.IsOpen = true;
    }
}
