using EchoPlay.App.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Favoriten-Abschnitt des Dashboards.
    /// Hält die Favoriten-Kacheln als <see cref="ObservableCollection{T}"/>, damit das ListView
    /// per Drag &amp; Drop die Reihenfolge umsortieren kann. Beim Umsortieren persistiert das VM
    /// die neue Reihenfolge in der DashboardPositions-Tabelle. Entfernt ein Nutzer eine Serie
    /// aus den Favoriten, wird die betroffene Kachel direkt aus der Sammlung entfernt.
    /// </summary>
    public sealed class DashboardFavoritenViewModel : ObservableObject, IDisposable
    {
        // Dashboard-Sektionsname für die Positionsspeicherung
        private const string SectionFavorites = "Favoriten";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        private ObservableCollection<FavoriteSeriesCardViewModel> _favoriteSeries = [];

        /// <summary>
        /// Initialisiert das Sub-VM mit DI-Scope-Fabrik und Logger.
        /// </summary>
        /// <param name="scopeFactory">Für das Speichern der Reihenfolge.</param>
        /// <param name="logger">Für Info-/Warning-Meldungen beim Reorder.</param>
        public DashboardFavoritenViewModel(IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Favorisierte Serien als Kachelreihe. <see cref="ObservableCollection{T}"/>, damit das
        /// ListView mit <c>CanReorderItems</c> die Sammlung direkt per Drag &amp; Drop umsortieren kann.
        /// </summary>
        public ObservableCollection<FavoriteSeriesCardViewModel> FavoriteSeries
        {
            get => _favoriteSeries;
            private set
            {
                // Alten Handler abmelden, damit kein Listener auf einer verwaisten Collection hängt
                _favoriteSeries.CollectionChanged -= OnFavoriteSeriesReordered;

                if (SetProperty(ref _favoriteSeries, value))
                {
                    OnPropertyChanged(nameof(FavoriteSectionVisibility));

                    // CollectionChanged feuert zuverlässig wenn ListView per Drag & Drop
                    // Items in der Collection verschiebt (Remove + Insert).
                    value.CollectionChanged += OnFavoriteSeriesReordered;
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Favoriten-Abschnitts – sichtbar sobald mindestens eine Serie da ist.
        /// </summary>
        public Visibility FavoriteSectionVisibility =>
            _favoriteSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Wird bei einer Nutzeränderung an der Favoriten-Sammlung ausgelöst (Drag &amp; Drop oder
        /// Entfernen einer Kachel), damit das Top-VM abgeleitete Visibilities neu berechnen kann
        /// (z.B. <c>NoFavoritesHintVisibility</c>).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM-interner Parent/Sub-VM-Bridge-Trigger ohne Nutzdaten: Action-Signatur bleibt semantisch klarer als ein leerer EventArgs-Wrapper, da der Handler im Parent-VM nur abgeleitete Visibilities neu berechnet.")]
        public event Action? FavoritesChanged;

        /// <summary>
        /// Ersetzt die Favoriten-Kacheln mit der übergebenen Liste und verdrahtet die
        /// <c>RemovedFromFavorites</c>-Events aller Kacheln.
        /// </summary>
        /// <param name="items">Die vom Top-VM vorbereiteten, bereits sortierten Kacheln.</param>
        public void SetItems(IReadOnlyList<FavoriteSeriesCardViewModel> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            // Alte Karten-Handler vor dem Ersetzen lösen — bei wiederholtem SetItems mit
            // denselben Card-Instanzen (z.B. Dashboard-Reload) würden sonst Handler
            // akkumulieren. Der CollectionChanged-Handler auf der ObservableCollection
            // selbst wird im Setter von FavoriteSeries bereits sauber umgehängt.
            foreach (FavoriteSeriesCardViewModel existing in _favoriteSeries)
            {
                existing.RemovedFromFavorites -= OnSeriesRemovedFromFavorites;
            }

            foreach (FavoriteSeriesCardViewModel card in items)
            {
                card.RemovedFromFavorites += OnSeriesRemovedFromFavorites;
            }

            FavoriteSeries = new ObservableCollection<FavoriteSeriesCardViewModel>(items);
            FavoritesChanged?.Invoke();
        }

        /// <summary>
        /// Reagiert auf das Entfernen einer Serie aus den Favoriten. Entfernt die Kachel direkt
        /// aus der Sammlung, damit die UI sofort reagiert. Die Sichtbarkeit wird manuell
        /// aktualisiert, weil das direkte Remove nicht den Property-Setter durchläuft.
        /// </summary>
        /// <param name="seriesId">Die ID der entfernten Serie.</param>
        private void OnSeriesRemovedFromFavorites(Guid seriesId)
        {
            FavoriteSeriesCardViewModel? cardToRemove = null;

            foreach (FavoriteSeriesCardViewModel card in _favoriteSeries)
            {
                if (card.SeriesId == seriesId)
                {
                    cardToRemove = card;
                    break;
                }
            }

            if (cardToRemove is not null)
            {
                cardToRemove.RemovedFromFavorites -= OnSeriesRemovedFromFavorites;
                _ = _favoriteSeries.Remove(cardToRemove);
                OnPropertyChanged(nameof(FavoriteSectionVisibility));
                FavoritesChanged?.Invoke();
            }
        }

        /// <summary>
        /// Reagiert auf Umsortierungen der Favoriten-Sammlung durch das ListView.
        /// <see cref="ObservableCollection{T}"/> meldet zwei Events (Remove + Add) – gespeichert
        /// wird nur beim Add, um Doppel-Speicherungen zu vermeiden.
        /// </summary>
        private void OnFavoriteSeriesReordered(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                _logger.Info("Favoriten umsortiert – speichere neue Reihenfolge.");
                _ = SaveFavoriteSeriesOrderAsync();
            }
        }

        /// <summary>
        /// Speichert die aktuelle Reihenfolge der Favoriten-Kacheln über den
        /// <see cref="IDashboardPositionDataService"/> in der Datenbank.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SaveFavoriteSeriesOrderAsync()
        {
            try
            {
                _logger.Info($"Speichere Favoriten-Reihenfolge ({_favoriteSeries.Count} Serien).");

                List<Guid> seriesIds = new(_favoriteSeries.Count);
                foreach (FavoriteSeriesCardViewModel card in _favoriteSeries)
                {
                    seriesIds.Add(card.SeriesId);
                    _logger.Debug(() => $"'{card.SeriesName}' → Position {seriesIds.Count - 1}");
                }

                using IServiceScope scope = _scopeFactory.CreateScope();
                IDashboardPositionDataService positionService =
                    scope.ServiceProvider.GetRequiredService<IDashboardPositionDataService>();

                await positionService.SaveOrderAsync(SectionFavorites, seriesIds);

                _logger.Info("Favoriten-Reihenfolge gespeichert.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning($"Favoriten-Reihenfolge konnte nicht gespeichert werden: {ex.Message}");
            }
        }

        /// <summary>
        /// Löst alle Event-Subscriptions und verhindert Memory-Leaks beim Verlassen der Seite.
        /// </summary>
        public void Dispose()
        {
            _favoriteSeries.CollectionChanged -= OnFavoriteSeriesReordered;
            foreach (FavoriteSeriesCardViewModel card in _favoriteSeries)
            {
                card.RemovedFromFavorites -= OnSeriesRemovedFromFavorites;
            }
        }
    }
}
