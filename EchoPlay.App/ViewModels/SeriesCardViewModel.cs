using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für eine Serie in der Online-Mediathek.
    /// Enthält Episodenzähler, Abonnementstatus und den Befehl zum Umschalten des Abonnements.
    /// </summary>
    public sealed class SeriesCardViewModel : ObservableObject, IAccordionSelectable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ILocalizationService _localizationService;

        private bool _isSubscribed;
        private bool _isFavorite;
        private bool _isWatched;
        private bool _isSelectedInAccordion;
        private BitmapImage? _coverImage;

        /// <summary>
        /// Initialisiert das Kachel-ViewModel mit Stammdaten und Services.
        /// </summary>
        /// <param name="id">Datenbank-ID der Serie.</param>
        /// <param name="title">Titel der Serie.</param>
        /// <param name="coverImage">Vorschaubild oder null.</param>
        /// <param name="totalEpisodeCount">Gesamtanzahl der Episoden.</param>
        /// <param name="newEpisodeCount">Anzahl noch nicht angehörter Episoden (NotStarted).</param>
        /// <param name="inProgressCount">Anzahl begonnener Episoden (InProgress).</param>
        /// <param name="finishedCount">Anzahl vollständig gehörter Episoden (Finished).</param>
        /// <param name="isSubscribed">Aktueller Abonnementstatus.</param>
        /// <param name="isFavorite">Aktueller Favoritenstatus.</param>
        /// <param name="isWatched">Aktueller Überwachungsstatus für Neuerscheinungen.</param>
        /// <param name="scopeFactory">Für DB-Zugriffe im Toggle-Command.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge.</param>
        /// <param name="localizationService">Für lokalisierte Dialog-Texte.</param>
        public SeriesCardViewModel(
            Guid id,
            string title,
            BitmapImage? coverImage,
            int totalEpisodeCount,
            int newEpisodeCount,
            int inProgressCount,
            int finishedCount,
            bool isSubscribed,
            bool isFavorite,
            bool isWatched,
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            ILocalizationService localizationService)
        {
            Id = id;
            Title = title;
            _coverImage = coverImage;
            TotalEpisodeCount = totalEpisodeCount;
            NewEpisodeCount = newEpisodeCount;
            InProgressCount = inProgressCount;
            FinishedCount = finishedCount;
            _isSubscribed = isSubscribed;
            _isFavorite = isFavorite;
            _isWatched = isWatched;

            _scopeFactory = scopeFactory;
            _confirmationDialogService = confirmationDialogService;
            _localizationService = localizationService;

            ToggleSubscriptionCommand = new RelayCommand(() => _ = ToggleSubscriptionAsync());
            ToggleFavoriteCommand = new RelayCommand(() => _ = ToggleFavoriteAsync());
        }

        /// <summary>Datenbank-ID der Serie.</summary>
        public Guid Id { get; }

        /// <summary>Titel der Serie.</summary>
        public string Title { get; }

        /// <summary>
        /// Vorschaubild der Serie.
        /// Null, wenn weder lokale Coverdaten noch eine URL verfügbar sind.
        /// </summary>
        /// <summary>
        /// Cover-Bild der Serie – wird ggf. nachträglich gesetzt wenn das Cover
        /// aus der Provider-URL heruntergeladen und gecacht wurde.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    OnPropertyChanged(nameof(NoCoverVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Cover-Platzhalters wenn kein Bild vorhanden.</summary>
        public Visibility NoCoverVisibility =>
            _coverImage is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Setzt das Cover-Bild zurück. Wird beim Ersetzen der Online-Mediathek-Liste
        /// oder beim Verlassen der Page aufgerufen, damit hunderte Kachel-Bitmaps nicht
        /// bis zum nächsten GC-Lauf am Heap kleben (Brief 269).
        /// </summary>
        public void ClearCoverImage()
        {
            CoverImage = null;
        }

        /// <summary>Gesamtanzahl der Episoden dieser Serie.</summary>
        public int TotalEpisodeCount { get; }

        /// <summary>
        /// Kompakter Zählertext für das Overlay auf der Kachel.
        /// Zeigt gehörte/gesamte Episoden – analog zur lokalen Mediathek.
        /// </summary>
        public string CountText => $"{FinishedCount} / {TotalEpisodeCount}";

        /// <summary>Anzahl noch nicht angehörter Episoden.</summary>
        public int NewEpisodeCount { get; }

        /// <summary>Anzahl begonnener aber noch nicht abgeschlossener Episoden.</summary>
        public int InProgressCount { get; }

        /// <summary>Anzahl vollständig gehörter Episoden.</summary>
        public int FinishedCount { get; }

        /// <summary>
        /// Gibt an, ob die Serie abonniert ist.
        /// Ändert sich nach einem erfolgreichen Toggle-Befehl.
        /// </summary>
        public bool IsSubscribed
        {
            get => _isSubscribed;
            private set
            {
                if (SetProperty(ref _isSubscribed, value))
                {
                    OnPropertyChanged(nameof(StarVisibility));
                }
            }
        }

        /// <summary>
        /// Steuert die Sichtbarkeit des Stern-Icons auf der Kachel.
        /// Nur abonnierte Serien zeigen das Stern-Symbol.
        /// </summary>
        public Visibility StarVisibility =>
            _isSubscribed ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob diese Serie im Akkordeon aufgeklappt ist.
        /// Steuert das V-Icon unter der Kachel.
        /// </summary>
        public bool IsSelectedInAccordion
        {
            get => _isSelectedInAccordion;
            set
            {
                if (SetProperty(ref _isSelectedInAccordion, value))
                {
                    OnPropertyChanged(nameof(SelectedIndicatorVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des V-Pfeils unter der Kachel.</summary>
        public Visibility SelectedIndicatorVisibility =>
            _isSelectedInAccordion ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob mindestens eine Episode noch nicht angehört wurde.
        /// Wird für den <see cref="SeriesStatusFilter.Neu"/>-Filter verwendet.
        /// </summary>
        public bool HasNewEpisodes => NewEpisodeCount > 0;

        /// <summary>
        /// Gibt an, ob mindestens eine Episode angefangen aber noch nicht fertig gehört wurde.
        /// Wird für den <see cref="SeriesStatusFilter.AmHoeren"/>-Filter verwendet.
        /// </summary>
        public bool HasInProgressEpisodes => InProgressCount > 0;

        /// <summary>
        /// Gibt an, ob alle Episoden dieser Serie vollständig gehört wurden.
        /// Wird für den <see cref="SeriesStatusFilter.Gehört"/>-Filter verwendet.
        /// </summary>
        public bool AllEpisodesFinished =>
            TotalEpisodeCount > 0 && FinishedCount == TotalEpisodeCount;

        /// <summary>
        /// Gibt an, ob die Serie als Favorit markiert ist.
        /// Der Stern auf der Kachel wechselt zwischen gefüllt und leer.
        /// </summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            private set
            {
                if (SetProperty(ref _isFavorite, value))
                {
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        /// <summary>
        /// Stern-Symbol für die Kachel: gefüllt (★) wenn Favorit, leer (☆) wenn nicht.
        /// Segoe Fluent Icons: E735 = FavoriteStarFill (gefüllt), E734 = FavoriteStar (leer).
        /// </summary>
        public string FavoriteGlyph => _isFavorite ? "\uE735" : "\uE734";

        /// <summary>Schaltet den Favoritenstatus um und persistiert die Änderung in der Datenbank.</summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>
        /// Gibt an, ob die Serie auf Neuerscheinungen überwacht wird.
        /// Steuert das Auge-Icon auf der Kachel und den Dashboard-Filter.
        /// </summary>
        public bool IsWatched
        {
            get => _isWatched;
            set
            {
                if (SetProperty(ref _isWatched, value))
                {
                    OnPropertyChanged(nameof(WatchedGlyph));
                    OnPropertyChanged(nameof(WatchedVisibility));
                }
            }
        }

        /// <summary>
        /// Auge-Symbol für die Kachel: gefüllt wenn überwacht, leer wenn nicht.
        /// Segoe Fluent Icons: E7B3 = RedEye.
        /// </summary>
        public string WatchedGlyph => "\uE7B3";

        /// <summary>
        /// Sichtbarkeit des Überwachungs-Icons: nur bei überwachten Serien eingeblendet.
        /// </summary>
        public Visibility WatchedVisibility =>
            _isWatched ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Schaltet das Abonnement um und zeigt vorher einen Bestätigungs-Dialog.</summary>
        public ICommand ToggleSubscriptionCommand { get; }

        /// <summary>
        /// Zeigt einen Bestätigungs-Dialog und aktualisiert das Abonnement in der Datenbank.
        /// Der Toggle-Befehl funktioniert in beide Richtungen: Abonnieren und Kündigen.
        /// </summary>
        private async Task ToggleSubscriptionAsync()
        {
            bool shouldSubscribe = !_isSubscribed;

            string title = shouldSubscribe
                ? _localizationService.Get("OnlineSubscribeDialogTitle")
                : _localizationService.Get("OnlineUnsubscribeDialogTitle");
            string message = shouldSubscribe
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _localizationService.Get("OnlineSubscribeDialogMessage"), Title)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _localizationService.Get("OnlineUnsubscribeDialogMessage"), Title);

            bool confirmed = await _confirmationDialogService.ConfirmAsync(title, message);

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService service = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await service.SetSubscribedAsync(Id, shouldSubscribe);

            IsSubscribed = shouldSubscribe;
        }

        /// <summary>
        /// Schaltet den Favoritenstatus um: Favorit → kein Favorit, kein Favorit → Favorit.
        /// Speichert den neuen Status sofort in der Datenbank.
        /// </summary>
        private async Task ToggleFavoriteAsync()
        {
            bool newValue = !_isFavorite;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            await seriesService.SetFavoriteAsync(Id, newValue);
            IsFavorite = newValue;
        }
    }
}
