using EchoPlay.App.Infrastructure;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Online-Suche nach Hörspielserien.
    /// Unterstützt Suche per Eingabetaste in der AutoSuggestBox oder per Schaltfläche.
    /// Suchergebnisse werden als Kachelgitter mit Import-Option dargestellt.
    ///
    /// Nimmt optionale Navigationsparameter entgegen:
    /// - "onboarding": zeigt Willkommens-Hinweis (erster Start)
    /// - beliebiger String: wird als Suchtext vorab eingetragen und die Suche gestartet
    /// </summary>
    public sealed partial class SuchePage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public SucheViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public SuchePage()
        {
            ViewModel = App.Services.GetRequiredService<SucheViewModel>();
            InitializeComponent();
        }

        /// <summary>
        /// Leitet den Navigationsparameter ans ViewModel weiter. Das ViewModel prüft
        /// den Offline-Modus, zeigt ggf. einen Hinweis und navigiert zurück.
        /// </summary>
        /// <param name="e">Enthält den optionalen Navigationsparameter.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.InitializeAsync(e.Parameter));
        }

        /// <summary>
        /// Bricht laufende Cover-Loads der Trefferliste ab, damit verwaiste HTTP-Requests
        /// nicht mehr im Hintergrund weiterlaufen, wenn der Nutzer die Seite verlässt.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ViewModel.CancelPendingSearchCovers();
            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// Navigiert zur Online-Mediathek – wird aus dem Erfolgshinweis aufgerufen.
        /// </summary>
        private void OnGoToMediathekClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.NavigateToOnlineMediathek();
        }

        private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ViewModel.SearchText = args.QueryText;

            if (ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
        }
    }
}
