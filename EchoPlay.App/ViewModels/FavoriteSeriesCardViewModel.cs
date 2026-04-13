using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Globalization;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für den „Favoriten"-Abschnitt der Startseite.
    /// Repräsentiert eine Hörspielserie, die der Nutzer als Favorit markiert hat.
    /// </summary>
    public sealed class FavoriteSeriesCardViewModel
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ILocalizationService? _localizationService;

        /// <summary>
        /// Wird ausgelöst, nachdem die Serie erfolgreich aus den Favoriten entfernt wurde.
        /// Das Dashboard-ViewModel reagiert darauf und entfernt die Kachel aus der Liste.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Card-VM meldet genau eine Entity-ID an den Dashboard-Container; Action<Guid> bleibt semantisch klarer als ein kuenstlicher 'SeriesIdEventArgs'-Wrapper.")]
        public event Action<Guid>? RemovedFromFavorites;

        /// <summary>
        /// Initialisiert das ViewModel mit den nötigen Daten und Service-Abhängigkeiten.
        /// </summary>
        /// <param name="seriesId">Datenbank-ID der Serie, für die Navigation zur Detailseite.</param>
        /// <param name="seriesName">Titel der Serie.</param>
        /// <param name="coverImage">Coverbild der Serie, oder <see langword="null"/> wenn keines vorhanden ist.</param>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe beim Entfernen aus Favoriten.</param>
        /// <param name="confirmationDialogService">Für den Bestätigungsdialog.</param>
        /// <param name="localizationService">Liefert lokalisierte UI-Strings. Nullable für Tests.</param>
        public FavoriteSeriesCardViewModel(
            Guid seriesId,
            string seriesName,
            BitmapImage? coverImage,
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            ILocalizationService? localizationService = null)
        {
            SeriesId                    = seriesId;
            SeriesName                  = seriesName;
            CoverImage                  = coverImage;
            _scopeFactory               = scopeFactory;
            _confirmationDialogService  = confirmationDialogService;
            _localizationService        = localizationService;

            RemoveFromFavoritesCommand = new RelayCommand(() => _ = RemoveFromFavoritesAsync());
        }

        /// <summary>Datenbank-ID der Serie – wird bei der Navigation zur Detailseite übergeben.</summary>
        public Guid SeriesId { get; }

        /// <summary>Titel der Serie, z.B. „Die drei ???".</summary>
        public string SeriesName { get; }

        /// <summary>
        /// Coverbild der Serie.
        /// <see langword="null"/> wenn weder lokal gespeicherte Bilddaten noch eine Cover-URL vorhanden sind.
        /// </summary>
        public BitmapImage? CoverImage { get; }

        /// <summary>
        /// Entfernt die Serie aus den Favoriten (nach Bestätigung durch den Nutzer).
        /// </summary>
        public ICommand RemoveFromFavoritesCommand { get; }

        /// <summary>
        /// Fragt den Nutzer per Bestätigungsdialog und entfernt die Serie bei Zustimmung aus den Favoriten.
        /// Löst anschließend <see cref="RemovedFromFavorites"/> aus, damit das Dashboard die Kachel entfernen kann.
        /// </summary>
        private async System.Threading.Tasks.Task RemoveFromFavoritesAsync()
        {
            string title = _localizationService?.Get("FavoriteRemoveTitle") ?? "Aus Favoriten entfernen?";
            string message = _localizationService is not null
                ? string.Format(CultureInfo.CurrentCulture, _localizationService.Get("FavoriteRemoveMessage"), SeriesName)
                : $"\u201E{SeriesName}\u201C wird nicht mehr als Favorit angezeigt.";

            bool confirmed = await _confirmationDialogService.ConfirmAsync(title, message);

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            await seriesService.SetFavoriteAsync(SeriesId, false);
            RemovedFromFavorites?.Invoke(SeriesId);
        }
    }
}
