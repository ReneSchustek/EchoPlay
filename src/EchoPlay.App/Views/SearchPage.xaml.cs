using EchoPlay.App.Infrastructure;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Online-Suche nach Hörspielserien.
    /// Unterstützt Suche per Eingabetaste im Suchfeld oder über die Aktion im Seitenkopf.
    /// Suchergebnisse werden als Kachelgitter mit Import-Option dargestellt.
    ///
    /// Nimmt optionale Navigationsparameter entgegen:
    /// - "onboarding": zeigt Willkommens-Hinweis (erster Start)
    /// - beliebiger String: wird als Suchtext vorab eingetragen und die Suche gestartet
    /// </summary>
    public sealed partial class SearchPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public SearchViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public SearchPage()
        {
            ViewModel = App.Services.GetRequiredService<SearchViewModel>();
            InitializeComponent();

            InitializeHeaderAndScope();
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

        private void OnSearchSubmitted(object? sender, Controls.SearchSubmittedEventArgs e)
        {
            ViewModel.SearchText = e.Query;

            if (ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
        }

        private static readonly Helpers.SafeResourceStrings _resources = new();

        /// <summary>Schlüssel des Suchbereichs über alle Quellen.</summary>
        private const string ScopeKeyBoth = "both";

        /// <summary>Schlüssel des Suchbereichs beim Anbieter.</summary>
        private const string ScopeKeyOnline = "online";

        /// <summary>Schlüssel des Suchbereichs im eigenen Bestand.</summary>
        private const string ScopeKeyLocal = "local";

        /// <summary>Die Chips des Suchbereichs — genau einer ist immer aktiv.</summary>
        private Controls.FilterChip[] _scopeChips = [];

        /// <summary>
        /// Beschriftet Seitenkopf, Suchfeld und Suchbereich. Die Texte stehen im Quelltext und
        /// nicht als x:Uid, weil x:Uid nur die Eigenschaften eingebauter Steuerelemente bedient.
        /// </summary>
        private void InitializeHeaderAndScope()
        {
            SucheHeader.Title = _resources.GetString("SearchPageTitle");
            SucheHeader.Subtitle = _resources.GetString("SearchPageSubtitle");
            SucheHeader.ActionText = _resources.GetString("SucheAction");

            SucheSearchField.PlaceholderText = _resources.GetString("SucheSearchPlaceholder");

            _scopeChips =
            [
                new Controls.FilterChip(ScopeKeyBoth, _resources.GetString("SucheScopeBothLabel"), isActive: true),
                new Controls.FilterChip(ScopeKeyOnline, _resources.GetString("SucheScopeOnlineLabel")),
                new Controls.FilterChip(ScopeKeyLocal, _resources.GetString("SucheScopeLocalLabel"))
            ];

            SucheScopeBar.Chips = _scopeChips;
        }

        /// <summary>
        /// Übernimmt den gewählten Suchbereich. Anders als bei einem Filter bleibt hier immer
        /// einer aktiv: Ohne Bereich gäbe es nichts zu durchsuchen. Der Klick auf den bereits
        /// aktiven Chip schaltet ihn deshalb sofort wieder ein.
        /// </summary>
        private void OnScopeToggled(object? sender, Controls.FilterToggledEventArgs e)
        {
            if (!e.Chip.IsActive)
            {
                e.Chip.IsActive = true;
                return;
            }

            foreach (Controls.FilterChip chip in _scopeChips)
            {
                if (!ReferenceEquals(chip, e.Chip))
                {
                    chip.IsActive = false;
                }
            }

            ViewModel.SelectedScopeIndex = e.Chip.Key switch
            {
                ScopeKeyOnline => 1,
                ScopeKeyLocal => 2,
                _ => 0
            };
        }
    }
}
