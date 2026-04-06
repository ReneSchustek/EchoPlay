using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Pages
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
        /// Wertet den Navigationsparameter aus.
        /// "onboarding" aktiviert den Willkommens-Hinweis.
        /// Jeder andere nicht-leere String wird als Suchtext vorausgefüllt und die Suche direkt gestartet.
        /// </summary>
        /// <param name="e">Enthält den optionalen Navigationsparameter.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Offline-Modus: Suche benötigt Internetverbindung → Hinweis und zurück
            using (IServiceScope scope = App.Services.CreateScope())
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();

                if (settings.OfflineMode)
                {
                    IErrorDialogService dialogService = App.Services
                        .GetRequiredService<IErrorDialogService>();
                    await dialogService.ShowAsync(
                        Windows.ApplicationModel.Resources.ResourceLoader
                            .GetForViewIndependentUse().GetString("OfflineModeSearchHintTitle"),
                        Windows.ApplicationModel.Resources.ResourceLoader
                            .GetForViewIndependentUse().GetString("OfflineModeSearchHintMessage"));

                    if (Frame.CanGoBack)
                    {
                        Frame.GoBack();
                    }

                    return;
                }
            }

            if (e.Parameter is not string parameter || string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            if (parameter == "onboarding")
            {
                // Erster Start – Nutzer hat noch keine Serien abonniert
                ViewModel.IsOnboardingHintVisible = true;
                return;
            }

            // Suchtext aus einem anderen NavigationView-Eintrag (z.B. MediathekOnline QuerySubmitted)
            ViewModel.SearchText = parameter;

            if (ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer in der AutoSuggestBox Enter drückt oder einen Vorschlag wählt.
        /// Setzt den Suchtext explizit, da QueryText und Text der AutoSuggestBox leicht abweichen können.
        /// </summary>
        /// <summary>
        /// Navigiert zur Online-Mediathek – wird aus dem Erfolgshinweis aufgerufen.
        /// </summary>
        private void OnGoToMediathekClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MediathekOnlinePage));
        }

        private void OnHelpClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => HelpTip.IsOpen = true;

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
