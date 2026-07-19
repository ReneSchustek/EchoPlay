using EchoPlay.Core.Models.Import;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert ein einzelnes Suchergebnis in der Import-Ansicht.
    /// Enthält alle für die Darstellung benötigten Informationen
    /// sowie den Befehl zum Starten des Imports.
    /// </summary>
    public sealed class ImportResultViewModel
    {
        /// <summary>
        /// Initialisiert das ViewModel.
        /// </summary>
        /// <param name="series">Die Quelldaten aus der Suchanfrage.</param>
        /// <param name="isAlreadyImported">True, wenn die Serie bereits in der Datenbank vorhanden ist.</param>
        /// <param name="importCommand">Befehl zum Importieren dieser Serie.</param>
        public ImportResultViewModel(ImportSeries series, bool isAlreadyImported, ICommand importCommand)
        {
            ArgumentNullException.ThrowIfNull(series);
            Series = series;
            Title = series.Title;
            ScoreText = $"Score: {series.Score}";
            IsAlreadyImported = isAlreadyImported;
            ImportCommand = importCommand;

            CoverImage = series.CoverImageUrl is not null
                ? new BitmapImage(new System.Uri(series.CoverImageUrl))
                : null;
        }

        /// <summary>Quelldaten aus der Suchanfrage.</summary>
        public ImportSeries Series { get; }

        /// <summary>Titel der Serie.</summary>
        public string Title { get; }

        /// <summary>Fachlicher Score als lesbarer Text, z.B. "Score: 42".</summary>
        public string ScoreText { get; }

        /// <summary>Coverbild der Serie, oder null wenn keine URL vorhanden.</summary>
        public BitmapImage? CoverImage { get; }

        /// <summary>Gibt an, ob die Serie bereits in der Datenbank vorhanden ist.</summary>
        public bool IsAlreadyImported { get; }

        /// <summary>
        /// Sichtbarkeit des Import-Buttons – ausgeblendet wenn bereits importiert.
        /// </summary>
        public Visibility ImportButtonVisibility =>
            IsAlreadyImported ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Sichtbarkeit des „Vorhanden"-Texts – eingeblendet wenn bereits importiert.
        /// </summary>
        public Visibility AlreadyImportedTextVisibility =>
            IsAlreadyImported ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Befehl zum Importieren dieser Serie.</summary>
        public ICommand ImportCommand { get; }
    }
}
