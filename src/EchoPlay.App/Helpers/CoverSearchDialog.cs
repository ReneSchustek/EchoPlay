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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Interne TriggerSearchAsync-Wrapper-Funktion fängt alle Fehler der externen Cover-Suche (HTTP/iTunes/CoverArtArchive-Provider) ab und zeigt lediglich eine neutrale Statusmeldung, damit der Dialog offen bleibt.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
        public static async Task<CoverSearchHit?> ShowAsync(
            string initialQuery,
            Func<string, EchoPlay.LocalLibrary.Cover.CoverSearchPage, CancellationToken, Task<IReadOnlyList<CoverSearchHit>>> searchFunc,
            XamlRoot xamlRoot)
        {
            // UI-Elemente aufbauen
            (Grid searchRow, TextBox queryBox, Button searchButton) = CreateSearchPanel(initialQuery);

            // Statuszeile: ProgressRing + Text
            ProgressRing progressRing = new()
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 6, 0),
                IsActive = false,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock statusText = new()
            {
                Text = string.Empty,
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

            // Ergebnis-Kacheln – VariableSizedWrapGrid statt GridView
            // (GridView im ContentDialog wirft COMException bei Item-Änderungen)
            VariableSizedWrapGrid resultsPanel = new()
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = TileWidth,
                ItemHeight = TileHeight,
                MaximumRowsOrColumns = MaxTilesPerRow
            };

            // Dialog zusammenbauen
            StackPanel content = new() { Spacing = 8, MinWidth = 400 };
            content.Children.Add(searchRow);
            content.Children.Add(statusRow);
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 400,
                Content = resultsPanel,
                Margin = new Thickness(0, 4, 0, 0)
            });

            ContentDialog dialog = new()
            {
                XamlRoot = xamlRoot,
                Title = SafeResourceLoader.Get("CoverSearchDialogTitle"),
                Content = content,
                PrimaryButtonText = SafeResourceLoader.Get("CommonApply"),
                CloseButtonText = SafeResourceLoader.Get("CommonCancel"),
                IsPrimaryButtonEnabled = false
            };

            // Zustand
            List<CoverSearchHit> currentResults = [];
            int selectedIndex = -1;

            // Zustand des Nachladens. Die Adressen dienen der Dubletten-Abwehr: Anbieter ohne
            // Versatz liefern beim Nachladen dieselben Treffer erneut, und zwei Anbieter können
            // dasselbe Bild kennen.
            EchoPlay.LocalLibrary.Cover.CoverSearchPage currentPage = EchoPlay.LocalLibrary.Cover.CoverSearchPage.First;
            HashSet<string> shownUrls = new(StringComparer.OrdinalIgnoreCase);
            Border? loadMoreTile = null;

            // Schließt der Anwender den Dialog während einer laufenden Suche, bricht die
            // Cover-Suche (HTTP-Request) über das Token früh ab – sonst läuft sie ungesehen weiter.
            using CancellationTokenSource dialogCts = new();
            dialog.Closing += (_, _) => dialogCts.Cancel();

            string searchingText = SafeResourceLoader.Get("CoverSearchSearching");
            progressRing.IsActive = true;
            statusText.Text = searchingText;

            // Suchfunktion
            async Task RunSearchAsync(string query)
            {
                progressRing.IsActive = true;
                statusText.Text = searchingText;
                currentResults.Clear();
                resultsPanel.Children.Clear();
                selectedIndex = -1;
                dialog.IsPrimaryButtonEnabled = false;
                currentPage = EchoPlay.LocalLibrary.Cover.CoverSearchPage.First;
                shownUrls.Clear();
                loadMoreTile = null;

                IReadOnlyList<CoverSearchHit> results =
                    await searchFunc(query.Trim(), currentPage, dialogCts.Token);

                progressRing.IsActive = false;

                if (results.Count == 0)
                {
                    statusText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        SafeResourceLoader.Get("CoverSearchNoResultsFormat"),
                        query.Trim());
                    return;
                }

                int neu = AppendResults(results);
                ShowHitCount();
                EnsureLoadMoreTile(neu > 0);
            }

            // Haengt Treffer an und liefert, wie viele davon neu waren. Gemeinsam genutzt von
            // der ersten Suche und vom Nachladen — sonst waere die Kachel-Erzeugung samt
            // Auswahl-Logik zweimal da.
            int AppendResults(IReadOnlyList<CoverSearchHit> results)
            {
                int neu = 0;

                foreach (CoverSearchHit r in results)
                {
                    if (!shownUrls.Add(r.FullUrl))
                    {
                        continue;
                    }

                    currentResults.Add(r);
                    int tileIndex = currentResults.Count - 1;
                    neu++;

                    Border tileBorder = CreateCoverTile(r);

                    tileBorder.PointerPressed += (_, _) =>
                    {
                        // Bisherige Auswahl zurücksetzen
                        foreach (UIElement child in resultsPanel.Children)
                        {
                            if (child is Border b)
                            {
                                b.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                            }
                        }

                        tileBorder.BorderBrush = (Brush)
                            Application.Current.Resources["AccentFillColorDefaultBrush"];
                        selectedIndex = tileIndex;
                        dialog.IsPrimaryButtonEnabled = true;
                    };

                    resultsPanel.Children.Add(tileBorder);
                }

                return neu;
            }

            void ShowHitCount()
            {
                statusText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    PluralText.Pattern(
                        currentResults.Count,
                        "CoverSearchHitsFoundSingular",
                        "CoverSearchHitsFoundPlural",
                        "{0} Treffer gefunden.",
                        "{0} Treffer gefunden."),
                    currentResults.Count);
            }

            // Die Nachlade-Kachel steht immer als letzte im Raster. Bleibt eine Runde ohne neue
            // Treffer, verschwindet sie — ein Knopf, der nichts mehr tut, ist schlimmer als
            // keiner.
            void EnsureLoadMoreTile(bool weitereMoeglich)
            {
                if (loadMoreTile is not null)
                {
                    _ = resultsPanel.Children.Remove(loadMoreTile);
                    loadMoreTile = null;
                }

                if (!weitereMoeglich)
                {
                    return;
                }

                Border tile = CreateLoadMoreTile();
                tile.PointerPressed += async (_, _) => await LoadMoreAsync();
                loadMoreTile = tile;
                resultsPanel.Children.Add(tile);
            }

            async Task LoadMoreAsync()
            {
                EnsureLoadMoreTile(weitereMoeglich: false);
                progressRing.IsActive = true;
                statusText.Text = searchingText;

                try
                {
                    currentPage = currentPage.Next;

                    IReadOnlyList<CoverSearchHit> weitere =
                        await searchFunc(queryBox.Text.Trim(), currentPage, dialogCts.Token);

                    int neu = AppendResults(weitere);
                    ShowHitCount();

                    if (neu == 0)
                    {
                        statusText.Text = SafeResourceLoader.Get(
                            "CoverSearchNoMoreResults", "Keine weiteren Treffer.");
                    }

                    EnsureLoadMoreTile(neu > 0);
                }
                catch (OperationCanceledException)
                {
                    // Dialog geschlossen — nichts zu tun.
                }
                catch (Exception)
                {
                    statusText.Text = SafeResourceLoader.Get("CoverSearchFailed");
                    EnsureLoadMoreTile(weitereMoeglich: true);
                }
                finally
                {
                    progressRing.IsActive = false;
                }
            }

            // Wrapper für Fehlerbehandlung
            async Task TriggerSearchAsync()
            {
                try
                {
                    await RunSearchAsync(queryBox.Text);
                }
                catch (OperationCanceledException)
                {
                    // Dialog wurde geschlossen — kein Fehlerzustand, einfach abbrechen.
                    progressRing.IsActive = false;
                }
                catch (Exception)
                {
                    progressRing.IsActive = false;
                    statusText.Text = SafeResourceLoader.Get("CoverSearchFailed");
                }
            }

            // Events
            searchButton.Click += async (_, _) => await TriggerSearchAsync();

            queryBox.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    await TriggerSearchAsync();
                }
            };

            dialog.Opened += async (_, _) => await TriggerSearchAsync();

            // Dialog anzeigen
            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult dialogResult = await dialog.ShowAsync();

            if (dialogResult == ContentDialogResult.Primary
                && selectedIndex >= 0
                && selectedIndex < currentResults.Count)
            {
                return currentResults[selectedIndex];
            }

            return null;
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
        private static Border CreateLoadMoreTile()
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
        private static Border CreateCoverTile(CoverSearchHit result)
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
