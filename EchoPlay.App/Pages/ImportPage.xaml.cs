using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace EchoPlay.App.Pages
{
    /// <summary>
    /// Ermöglicht die Suche nach Hörspielserien und den Import in die lokale Datenbank.
    /// Nach erfolgreichem Import wird zur Serienübersicht zurücknavigiert.
    /// </summary>
    public sealed partial class ImportPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public ImportViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public ImportPage()
        {
            ViewModel = App.Services.GetRequiredService<ImportViewModel>();
            InitializeComponent();

            ViewModel.ImportSucceeded += OnImportSucceeded;
        }

        /// <summary>
        /// Startet die Suche nach Betätigung des Suchen-Buttons.
        /// </summary>
        private async void OnSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.SearchAsync();
        }

        /// <summary>
        /// Startet die Suche bei Eingabe von Enter im Suchfeld.
        /// </summary>
        private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await ViewModel.SearchAsync();
            }
        }

        /// <summary>
        /// Navigiert zur Serienübersicht zurück, nachdem ein Import erfolgreich abgeschlossen wurde.
        /// Die Serienübersicht lädt ihre Daten beim OnNavigatedTo automatisch neu.
        /// </summary>
        private void OnImportSucceeded(object? sender, EventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }
}
