using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Seite „Angekündigt": was bei den vorhandenen Serien noch erscheint, nach Monat.
    /// </summary>
    public sealed partial class AnnouncedPage : Page
    {
        private static readonly Helpers.SafeResourceStrings _resources = new();

        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public AnnouncedViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht ViewModel und Navigation aus dem DI-Container.
        /// </summary>
        public AnnouncedPage()
        {
            ViewModel = App.Services.GetRequiredService<AnnouncedViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            AnnouncedHeader.Title = _resources.GetString("AnnouncedPageTitle");
            AnnouncedHeader.Subtitle = _resources.GetString("AnnouncedPageSubtitle");
        }

        /// <summary>Lädt die Liste beim Navigieren zur Seite.</summary>
        /// <param name="e">Navigationsdaten.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.LoadAsync());
        }

        /// <summary>Öffnet die Serie der angeklickten Kachel.</summary>
        /// <param name="sender">Die Kachelliste.</param>
        /// <param name="e">Trägt die angeklickte Kachel.</param>
        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ReleaseCalendarItemViewModel item)
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, item.SeriesId);
            }
        }
    }
}
