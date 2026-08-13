using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Wiederverwendbarer Cover-Such-Dialog – wird von lokaler und Online-Mediathek genutzt.
    /// Zeigt ein Suchfeld, Ergebnis-Kacheln und gibt den ausgewählten
    /// <see cref="CoverSearchHit"/> zurück. Der Helper kennt nur App-Modelle und ist
    /// nicht direkt von <see cref="EchoPlay.LocalLibrary.Cover"/> abhängig.
    /// </summary>
    internal static class CoverSearchDialog
    {
        /// <summary>Breite einer Ergebnis-Kachel in Pixeln.</summary>
        private const double TileWidth = 140;

        /// <summary>Höhe einer Ergebnis-Kachel in Pixeln.</summary>
        private const double TileHeight = 170;

        /// <summary>Maximale Anzahl Kacheln pro Zeile im Ergebnis-Panel.</summary>
        private const int MaxTilesPerRow = 3;

        /// <summary>Breite/Höhe des Cover-Bildes innerhalb einer Kachel.</summary>
        private const double CoverImageSize = 130;
        /// <summary>
        /// Zeigt den Cover-Such-Dialog und gibt das ausgewählte Ergebnis zurück.
        /// Null wenn der Dialog abgebrochen wurde oder kein Cover ausgewählt.
        /// </summary>
        /// <param name="initialQuery">Vorbelegter Suchbegriff (z.B. Folgentitel).</param>
        /// <param name="searchFunc">
        /// Suchfunktion – meist eine ViewModel-Methode, die intern den Cover-Suchdienst aufruft.
        /// Bekommt die gewünschte Seite mit; eine leere Antwort heißt „nichts mehr da".
        /// </param>
        /// <param name="xamlRoot">XamlRoot für den ContentDialog – kommt von der aufrufenden Page.</param>
        /// <returns>Das ausgewählte Cover oder null bei Abbruch.</returns>
        public static async Task<CoverSearchHit?> ShowAsync(
            string initialQuery,
            Func<string, EchoPlay.LocalLibrary.Cover.CoverSearchPage, CancellationToken, Task<IReadOnlyList<CoverSearchHit>>> searchFunc,
            XamlRoot xamlRoot)
        {
            (Grid searchRow, TextBox queryBox, Button searchButton) = CreateSearchPanel(initialQuery);
            (StackPanel statusRow, ProgressRing progressRing, TextBlock statusText) = CreateStatusRow();

            // VariableSizedWrapGrid statt GridView: Ein GridView im Dialog wirft eine
            // COM-Ausnahme, sobald sich seine Elemente ändern.
            VariableSizedWrapGrid resultsPanel = new()
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = TileWidth,
                ItemHeight = TileHeight,
                MaximumRowsOrColumns = MaxTilesPerRow
            };

            ContentDialog dialog = BuildDialog(xamlRoot, searchRow, statusRow, resultsPanel);

            // Schließt der Anwender den Dialog während einer laufenden Suche, bricht der
            // Abruf über das Zeichen früh ab — sonst läuft er ungesehen weiter.
            using CancellationTokenSource dialogCts = new();
            dialog.Closing += (_, _) => dialogCts.Cancel();

            CoverSearchSession session = new(
                searchFunc, dialog, queryBox, progressRing, statusText, resultsPanel, dialogCts.Token);

            searchButton.Click += async (_, _) => await session.SearchAsync();
            dialog.Opened += async (_, _) => await session.SearchAsync();
            queryBox.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    await session.SearchAsync();
                }
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult dialogResult = await dialog.ShowAsync();

            return dialogResult == ContentDialogResult.Primary ? session.SelectedHit : null;
        }

        /// <summary>
        /// Baut die Statuszeile aus Fortschrittsanzeige und Text.
        /// </summary>
        /// <returns>Die Zeile samt der beiden Teile, die die Sitzung später beschreibt.</returns>
        private static (StackPanel Row, ProgressRing Ring, TextBlock Text) CreateStatusRow()
        {
            ProgressRing progressRing = new()
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 6, 0),
                IsActive = true,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock statusText = new()
            {
                Text = SafeResourceLoader.Get("CoverSearchSearching"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            StackPanel statusRow = new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            statusRow.Children.Add(progressRing);
            statusRow.Children.Add(statusText);

            return (statusRow, progressRing, statusText);
        }

        /// <summary>
        /// Setzt den Dialog aus Suchzeile, Statuszeile und Ergebnisraster zusammen.
        /// </summary>
        private static ContentDialog BuildDialog(
            XamlRoot xamlRoot, Grid searchRow, StackPanel statusRow, VariableSizedWrapGrid resultsPanel)
        {
            StackPanel content = new() { Spacing = 8, MinWidth = 400 };
            content.Children.Add(searchRow);
            content.Children.Add(statusRow);
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 400,
                Content = resultsPanel,
                Margin = new Thickness(0, 4, 0, 0)
            });

            return new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = SafeResourceLoader.Get("CoverSearchDialogTitle"),
                Content = content,
                PrimaryButtonText = SafeResourceLoader.Get("CommonApply"),
                CloseButtonText = SafeResourceLoader.Get("CommonCancel"),
                IsPrimaryButtonEnabled = false
            };
        }

        /// <summary>
        /// Erstellt die Suchzeile mit TextBox und Button.
        /// </summary>
        /// <param name="initialQuery">Vorbelegter Suchbegriff für die TextBox.</param>
        /// <returns>Das fertige Grid sowie Referenzen auf TextBox und Button für Event-Verdrahtung.</returns>
        private static (Grid SearchRow, TextBox QueryBox, Button SearchButton) CreateSearchPanel(string initialQuery)
        {
            TextBox queryBox = new()
            {
                Text = initialQuery,
                PlaceholderText = SafeResourceLoader.Get("CoverSearchQueryPlaceholder"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            Button searchButton = new()
            {
                Content = SafeResourceLoader.Get("CoverSearchButton"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            };

            Grid searchRow = new() { ColumnSpacing = 0 };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(queryBox, 0);
            Grid.SetColumn(searchButton, 1);
            searchRow.Children.Add(queryBox);
            searchRow.Children.Add(searchButton);

            return (searchRow, queryBox, searchButton);
        }

        /// <summary>
        /// Baut die Kachel „Weitere Ergebnisse laden" — bewusst im Raster und nicht als Knopf
        /// darunter: Sie steht dort, wo die Treffer aufhören, und wird genau dann gesehen, wenn
        /// keiner gepasst hat.
        /// </summary>
        /// <returns>Die Kachel, ohne Klick-Behandlung.</returns>
        internal static Border CreateLoadMoreTile()
        {
            FontIcon icon = new()
            {
                Glyph = "",
                FontSize = 28,
                Width = CoverImageSize,
                Height = CoverImageSize
            };

            TextBlock label = new()
            {
                Text = SafeResourceLoader.Get("CoverSearchLoadMore", "Weitere Ergebnisse laden"),
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Width = CoverImageSize,
                Margin = new Thickness(4, 4, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            StackPanel tile = new();
            tile.Children.Add(icon);
            tile.Children.Add(label);

            return new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                Child = tile
            };
        }

        /// <summary>
        /// Erstellt eine einzelne Cover-Kachel mit Bild, Titel und Auswahlrahmen.
        /// </summary>
        /// <param name="result">Das Suchergebnis mit Thumbnail-URL und Titel.</param>
        /// <returns>Ein Border-Element, das als klickbare Kachel im Ergebnis-Panel dient.</returns>
        internal static Border CreateCoverTile(CoverSearchHit result)
        {
            Image coverImage = new()
            {
                Width = CoverImageSize,
                Height = CoverImageSize,
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(result.ThumbnailUrl))
            };

            TextBlock label = new()
            {
                Text = result.ReleaseTitle,
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10,
                Width = CoverImageSize,
                Margin = new Thickness(4, 4, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            StackPanel tile = new();
            tile.Children.Add(coverImage);
            tile.Children.Add(label);

            Border tileBorder = new()
            {
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                Child = tile
            };

            return tileBorder;
        }
    }
}
