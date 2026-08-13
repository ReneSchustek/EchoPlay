using EchoPlay.App.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Hinweise, mit denen die Startseite einen noch leeren Bestand erklärt.
    /// </summary>
    /// <remarks>
    /// Ohne sie sieht ein frisch eingerichtetes Programm aus wie ein kaputtes: Der
    /// Neuerscheinungs-Abschnitt bleibt leer, und nichts sagt warum. Die drei Zustände
    /// bauen aufeinander auf — keine Serie abonniert, keine favorisiert, keine überwacht —,
    /// deshalb stehen sie zusammen und nicht verteilt im Ansichtsmodell der Seite.
    /// </remarks>
    public sealed class DashboardOnboardingHints : ObservableObject
    {
        private bool _hasSubscribedSeries = true;
        private bool _hasFavoriteSeries;
        private bool _hasWatchedSeries;

        /// <summary>
        /// Gibt an, ob mindestens eine abonnierte Serie vorhanden ist. Ist keine da,
        /// führt die Startseite von sich aus zur Suche.
        /// </summary>
        public bool HasSubscribedSeries
        {
            get => _hasSubscribedSeries;
            private set
            {
                if (SetProperty(ref _hasSubscribedSeries, value))
                {
                    RaiseHintChanges();
                }
            }
        }

        /// <summary>
        /// Hinweis, wenn abonnierte Serien vorhanden sind, aber noch keine favorisiert wurde.
        /// </summary>
        public Visibility NoFavoritesHintVisibility =>
            _hasSubscribedSeries && !_hasFavoriteSeries ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Hinweis, wenn Favoriten existieren, aber keine einzige Serie überwacht wird.
        /// Ohne überwachte Serie fragt der Start gar nicht erst beim Anbieter nach und der
        /// Neuerscheinungs-Abschnitt verschwindet vollständig — der Hinweis macht diesen
        /// Zustand sichtbar, statt ihn wie einen Fehler wirken zu lassen.
        /// Der Fall tritt nur noch bei Altbeständen auf: Favorisieren schaltet die
        /// Überwachung inzwischen mit ein (siehe <see cref="ISeriesDataService.SetFavoriteAsync"/>).
        /// </summary>
        public Visibility NoWatchedSeriesHintVisibility =>
            _hasSubscribedSeries && _hasFavoriteSeries && !_hasWatchedSeries
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// Übernimmt den Stand aus der Datengrundlage eines Ladelaufs.
        /// </summary>
        /// <param name="snapshot">Die gelesene Datengrundlage.</param>
        internal void Apply(DashboardSnapshot snapshot)
        {
            System.ArgumentNullException.ThrowIfNull(snapshot);

            _hasFavoriteSeries = snapshot.HasFavoriteSeries;
            _hasWatchedSeries = snapshot.HasWatchedSeries;

            // Zuletzt gesetzt, weil die Eigenschaft die Meldungen selbst auslöst — so gilt
            // dabei schon der neue Stand der beiden anderen Werte.
            HasSubscribedSeries = snapshot.HasSubscribedSeries;
            RaiseHintChanges();
        }

        /// <summary>
        /// Zieht den Favoriten-Hinweis nach, wenn der Nutzer Favoriten entfernt oder umsortiert.
        /// </summary>
        /// <param name="hasFavoriteSeries">Ob danach noch mindestens ein Favorit übrig ist.</param>
        public void UpdateFavorites(bool hasFavoriteSeries)
        {
            _hasFavoriteSeries = hasFavoriteSeries;
            RaiseHintChanges();
        }

        /// <summary>Meldet beide Hinweis-Sichtbarkeiten als geändert.</summary>
        private void RaiseHintChanges()
        {
            OnPropertyChanged(nameof(NoFavoritesHintVisibility));
            OnPropertyChanged(nameof(NoWatchedSeriesHintVisibility));
        }
    }
}
