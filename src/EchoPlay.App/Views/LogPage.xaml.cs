using EchoPlay.App.ViewModels;
using EchoPlay.Logger.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

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
    public sealed partial class LogPage : Page
    {
        /// <summary>ViewModel – von XAML über x:Bind referenziert.</summary>
        public LogViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public LogPage()
        {
            ViewModel = App.Services.GetRequiredService<LogViewModel>();
            InitializeComponent();

            InitializeHeaderAndFilters();
        }

        private static readonly Helpers.SafeResourceStrings _resources = new();

        /// <summary>Die Chips der Stufen — gehalten, weil sie einander ausschließen.</summary>
        private Controls.FilterChip[] _levelChips = [];

        /// <summary>
        /// Beschriftet Seitenkopf, Suchfeld, Stufenfilter und den „Nichts gefunden"-Hinweis.
        /// Die Texte stehen im Quelltext und nicht als x:Uid, weil x:Uid nur die Eigenschaften
        /// eingebauter Steuerelemente bedient.
        /// </summary>
        private void InitializeHeaderAndFilters()
        {
            ProtokollHeader.Title = _resources.GetString("LogPageTitle");
            ProtokollHeader.Subtitle = _resources.GetString("LogPageSubtitle");
            ProtokollHeader.ActionText = _resources.GetString("ProtokollClearAction");

            ProtokollSearchField.PlaceholderText = _resources.GetString("ProtokollSearchPlaceholder");

            // Der Schlüssel ist der Name der Stufe — so bleibt die Zuordnung ohne eigene
            // Übersetzungstabelle nachvollziehbar.
            _levelChips =
            [
                new Controls.FilterChip(nameof(LogLevel.Error), _resources.GetString("ProtokollLevelError")),
                new Controls.FilterChip(nameof(LogLevel.Warning), _resources.GetString("ProtokollLevelWarning")),
                new Controls.FilterChip(nameof(LogLevel.Information), _resources.GetString("ProtokollLevelInformation")),
                new Controls.FilterChip(nameof(LogLevel.Debug), _resources.GetString("ProtokollLevelDebug"))
            ];

            ProtokollFilterBar.Chips = _levelChips;

            ProtokollNoResultsPanel.Message = _resources.GetString("ProtokollNoResultsMessage");
            ProtokollNoResultsPanel.ActionText = _resources.GetString("ProtokollResetFiltersAction");
            ProtokollNoResultsPanel.ActionCommand = ViewModel.ResetFiltersCommand;
        }

        /// <summary>
        /// Übernimmt die gewählte Stufe. Eine Meldung hat genau eine Stufe, deshalb geht mit
        /// dem neuen Chip jeder andere aus; ein zweiter Klick führt zurück zu „alle".
        /// </summary>
        private void OnFilterToggled(object? sender, Controls.FilterToggledEventArgs e)
        {
            foreach (Controls.FilterChip chip in _levelChips)
            {
                if (!ReferenceEquals(chip, e.Chip))
                {
                    chip.IsActive = false;
                }
            }

            ViewModel.LevelFilter = e.Chip.IsActive && Enum.TryParse(e.Chip.Key, out LogLevel level)
                ? level
                : null;
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
            ViewModel.Dispose();
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
