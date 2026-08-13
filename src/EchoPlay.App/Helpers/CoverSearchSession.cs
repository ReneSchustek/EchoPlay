using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Der Verlauf einer Cover-Suche im Dialog: Was gesucht wurde, welche Treffer schon
    /// stehen, welche Seite als Nächstes kommt und welche Kachel gewählt ist.
    /// </summary>
    /// <remarks>
    /// Das war eine Methode mit acht lokalen Funktionen über gemeinsamem Zustand — also eine
    /// Klasse, die als Methode geschrieben war. Als Typ ist derselbe Ablauf lesbar: Die
    /// Felder sagen, was die Sitzung sich merkt, und jede Methode hat einen Namen statt
    /// einer Stelle im Ablauf.
    /// </remarks>
    internal sealed class CoverSearchSession
    {
        private readonly Func<string, EchoPlay.LocalLibrary.Cover.CoverSearchPage, CancellationToken, Task<IReadOnlyList<CoverSearchHit>>> _searchFunc;
        private readonly ContentDialog _dialog;
        private readonly TextBox _queryBox;
        private readonly ProgressRing _progressRing;
        private readonly TextBlock _statusText;
        private readonly VariableSizedWrapGrid _resultsPanel;
        private readonly CancellationToken _cancellationToken;
        private readonly string _searchingText;

        private readonly List<CoverSearchHit> _currentResults = [];

        // Die Adressen wehren Dubletten ab: Anbieter ohne Versatz liefern beim Nachladen
        // dieselben Treffer erneut, und zwei Anbieter können dasselbe Bild kennen.
        private readonly HashSet<string> _shownUrls = new(StringComparer.OrdinalIgnoreCase);

        private EchoPlay.LocalLibrary.Cover.CoverSearchPage _currentPage =
            EchoPlay.LocalLibrary.Cover.CoverSearchPage.First;
        private Border? _loadMoreTile;
        private int _selectedIndex = -1;

        /// <summary>
        /// Richtet die Sitzung auf den fertig aufgebauten Dialog ein.
        /// </summary>
        /// <param name="searchFunc">Die eigentliche Suche; bekommt die gewünschte Seite mit.</param>
        /// <param name="dialog">Der Dialog — die Sitzung schaltet dessen Übernehmen-Schaltfläche.</param>
        /// <param name="queryBox">Das Suchfeld.</param>
        /// <param name="progressRing">Die Fortschrittsanzeige der Statuszeile.</param>
        /// <param name="statusText">Der Text der Statuszeile.</param>
        /// <param name="resultsPanel">Das Raster der Ergebnis-Kacheln.</param>
        /// <param name="cancellationToken">Endet mit dem Dialog und bricht die laufende Suche ab.</param>
        public CoverSearchSession(
            Func<string, EchoPlay.LocalLibrary.Cover.CoverSearchPage, CancellationToken, Task<IReadOnlyList<CoverSearchHit>>> searchFunc,
            ContentDialog dialog,
            TextBox queryBox,
            ProgressRing progressRing,
            TextBlock statusText,
            VariableSizedWrapGrid resultsPanel,
            CancellationToken cancellationToken)
        {
            _searchFunc = searchFunc;
            _dialog = dialog;
            _queryBox = queryBox;
            _progressRing = progressRing;
            _statusText = statusText;
            _resultsPanel = resultsPanel;
            _cancellationToken = cancellationToken;
            _searchingText = SafeResourceLoader.Get("CoverSearchSearching");
        }

        /// <summary>
        /// Das gewählte Cover, oder <see langword="null"/>, wenn nichts gewählt wurde.
        /// </summary>
        public CoverSearchHit? SelectedHit =>
            _selectedIndex >= 0 && _selectedIndex < _currentResults.Count
                ? _currentResults[_selectedIndex]
                : null;

        /// <summary>
        /// Sucht mit dem Text aus dem Suchfeld. Fehler bleiben im Dialog: Er soll offen
        /// bleiben, damit der Nutzer es mit einem anderen Begriff versuchen kann.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Treffer stehen oder eine Meldung erscheint.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Suche über fremde Gegenstellen (iTunes, CoverArtArchive, Discogs): HTTP-, Parse- oder Provider-Fehler dürfen den Dialog nicht schließen; der Nutzer bekommt eine neutrale Statusmeldung und kann es erneut versuchen.")]
        public async Task SearchAsync()
        {
            try
            {
                await RunSearchAsync(_queryBox.Text);
            }
            catch (OperationCanceledException)
            {
                // Dialog wurde geschlossen — kein Fehlerzustand.
                _progressRing.IsActive = false;
            }
            catch (Exception)
            {
                _progressRing.IsActive = false;
                _statusText.Text = SafeResourceLoader.Get("CoverSearchFailed");
            }
        }

        /// <summary>Setzt die Liste zurück und holt die erste Seite.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Das Muster kommt zur Laufzeit aus den Ressourcen und ist zum Übersetzungszeitpunkt unbekannt.")]
        private async Task RunSearchAsync(string query)
        {
            _progressRing.IsActive = true;
            _statusText.Text = _searchingText;
            _currentResults.Clear();
            _resultsPanel.Children.Clear();
            _selectedIndex = -1;
            _dialog.IsPrimaryButtonEnabled = false;
            _currentPage = EchoPlay.LocalLibrary.Cover.CoverSearchPage.First;
            _shownUrls.Clear();
            _loadMoreTile = null;

            IReadOnlyList<CoverSearchHit> results =
                await _searchFunc(query.Trim(), _currentPage, _cancellationToken);

            _progressRing.IsActive = false;

            if (results.Count == 0)
            {
                _statusText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    SafeResourceLoader.Get("CoverSearchNoResultsFormat"),
                    query.Trim());
                return;
            }

            int neue = AppendResults(results);
            ShowHitCount();
            EnsureLoadMoreTile(neue > 0);
        }

        /// <summary>
        /// Holt die nächste Seite. Bleibt sie ohne neue Treffer, verschwindet die
        /// Nachlade-Kachel — ein Knopf, der nichts mehr tut, ist schlimmer als keiner.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nachladen weiterer Cover: Fehler der fremden Gegenstelle dürfen den Dialog nicht schließen; die Nachlade-Kachel bleibt stehen, damit der Nutzer es erneut versuchen kann.")]
        private async Task LoadMoreAsync()
        {
            EnsureLoadMoreTile(morePossible: false);
            _progressRing.IsActive = true;
            _statusText.Text = _searchingText;

            try
            {
                _currentPage = _currentPage.Next;

                IReadOnlyList<CoverSearchHit> weitere =
                    await _searchFunc(_queryBox.Text.Trim(), _currentPage, _cancellationToken);

                int neue = AppendResults(weitere);
                ShowHitCount();

                if (neue == 0)
                {
                    _statusText.Text = SafeResourceLoader.Get(
                        "CoverSearchNoMoreResults", "Keine weiteren Treffer.");
                }

                EnsureLoadMoreTile(neue > 0);
            }
            catch (OperationCanceledException)
            {
                // Dialog geschlossen — nichts zu tun.
            }
            catch (Exception)
            {
                _statusText.Text = SafeResourceLoader.Get("CoverSearchFailed");
                EnsureLoadMoreTile(morePossible: true);
            }
            finally
            {
                _progressRing.IsActive = false;
            }
        }

        /// <summary>
        /// Hängt Treffer an und liefert, wie viele davon neu waren. Erste Suche und
        /// Nachladen teilen sich diesen Weg — sonst stünde der Kachelbau samt Auswahl
        /// zweimal da.
        /// </summary>
        private int AppendResults(IReadOnlyList<CoverSearchHit> results)
        {
            int neue = 0;

            foreach (CoverSearchHit treffer in results)
            {
                if (!_shownUrls.Add(treffer.FullUrl))
                {
                    continue;
                }

                _currentResults.Add(treffer);
                int tileIndex = _currentResults.Count - 1;
                neue++;

                Border tileBorder = CoverSearchDialog.CreateCoverTile(treffer);
                tileBorder.PointerPressed += (_, _) => Select(tileBorder, tileIndex);
                _resultsPanel.Children.Add(tileBorder);
            }

            return neue;
        }

        /// <summary>Hebt die gewählte Kachel hervor und gibt die Übernehmen-Schaltfläche frei.</summary>
        private void Select(Border tileBorder, int tileIndex)
        {
            foreach (UIElement child in _resultsPanel.Children)
            {
                if (child is Border rahmen)
                {
                    rahmen.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }

            tileBorder.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            _selectedIndex = tileIndex;
            _dialog.IsPrimaryButtonEnabled = true;
        }

        /// <summary>Schreibt die Trefferzahl in die Statuszeile.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Das Muster kommt zur Laufzeit aus den Ressourcen und ist zum Übersetzungszeitpunkt unbekannt.")]
        private void ShowHitCount()
        {
            _statusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                PluralText.Pattern(
                    _currentResults.Count,
                    "CoverSearchHitsFoundSingular",
                    "CoverSearchHitsFoundPlural",
                    "{0} Treffer gefunden.",
                    "{0} Treffer gefunden."),
                _currentResults.Count);
        }

        /// <summary>
        /// Hält die Nachlade-Kachel als letzte im Raster — oder entfernt sie, wenn nichts
        /// mehr nachzuladen ist.
        /// </summary>
        private void EnsureLoadMoreTile(bool morePossible)
        {
            if (_loadMoreTile is not null)
            {
                _ = _resultsPanel.Children.Remove(_loadMoreTile);
                _loadMoreTile = null;
            }

            if (!morePossible)
            {
                return;
            }

            Border tile = CoverSearchDialog.CreateLoadMoreTile();
            tile.PointerPressed += async (_, _) => await LoadMoreAsync();
            _loadMoreTile = tile;
            _resultsPanel.Children.Add(tile);
        }
    }
}
