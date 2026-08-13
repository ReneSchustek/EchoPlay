using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Serien-Detailansicht.
    /// Zeigt Episoden als sortierbare Kacheln und lädt beim Anwählen einer Episode
    /// die zugehörigen lokalen Tracks in die zweite Spalte.
    /// Online-Serien ohne lokale Dateien zeigen eine entsprechende Leer-Meldung.
    /// </summary>
    /// <remarks>
    /// Die Folgenliste samt Reiter, Filter, Suche und Sortierung führt
    /// <see cref="EpisodeList"/>; das Lesen und Bauen der Kacheln übernimmt der
    /// <see cref="SeriesEpisodeTileBuilder"/>. Hier bleibt, was die Seite als Ganzes
    /// betrifft: die Kopfzeile der Serie, die gewählte Folge samt ihren Spuren, die
    /// Wiedergabe und der Lebenszyklus der Seite.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Der einzige verwerfbare Zustand (_priorityCts) wird über CancelPendingPriorityLoad deterministisch freigegeben, das vom OnNavigatedFrom-Pfad der Page aufgerufen wird; ein eigener Dispose-Kontrakt im Transient-VM würde der bestehenden VM-Konvention widersprechen.")]
    public sealed class SeriesDetailViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPlayerService _playerService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly IClock _clock;
        private readonly SeriesEpisodeTileBuilder _tileBuilder;
        private CancellationTokenSource? _priorityCts;

        // Lifecycle-CTS: gilt für alle Service-Calls in dieser Detail-VM-Instanz.
        // Wird beim Page-Verlassen über Cleanup() gestoppt.
        private readonly CancellationTokenSource _lifecycleCts = new();

        private Guid _seriesId;
        private bool _isLoading;

        /// <summary>
        /// Initialisiert das ViewModel mit der Scope-Fabrik für Datenbankzugriffe.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für Scoped-Services.</param>
        /// <param name="playerService">Der zentrale Wiedergabe-Service.</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. Nullable für Tests.</param>
        /// <param name="backgroundCoverService">
        /// Optionaler Hintergrund-Cover-Service. Wenn gesetzt, priorisiert das VM
        /// die Folgen-Cover der geöffneten Serie und pausiert damit den laufenden
        /// Hintergrund-Scan, bis die sichtbare Serie versorgt ist.
        /// </param>
        /// <param name="localizationService">Für die Automation-Namen der Folgen-Kacheln. Nullable für Tests.</param>
        public SeriesDetailViewModel(
            IServiceScopeFactory scopeFactory,
            IPlayerService playerService,
            IClock clock,
            ICoverService? coverService = null,
            BackgroundCoverService? backgroundCoverService = null,
            ILocalizationService? localizationService = null)
        {
            _scopeFactory = scopeFactory;
            _playerService = playerService;
            _clock = clock;
            _backgroundCoverService = backgroundCoverService;
            _tileBuilder = new SeriesEpisodeTileBuilder(scopeFactory, coverService, localizationService);
            TrackList = new SeriesTrackList(scopeFactory);
            Header = new SeriesHeader(scopeFactory, _lifecycleCts.Token);

        }

        /// <summary>
        /// Die Kopfzeile: Titel, Beschreibung, Favoritenstern und Gesamtfortschritt.
        /// </summary>
        public SeriesHeader Header { get; }

        /// <summary>
        /// Die Folgenliste der Serie samt Reiter, Filter, Suche und Sortierung.
        /// </summary>
        public SeriesEpisodeList EpisodeList { get; } = new();

        /// <summary>
        /// Die Spurenspalte: gewählte Folge und ihre lokalen Dateien.
        /// </summary>
        public SeriesTrackList TrackList { get; }

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Sichtbarkeit des Leer-Hinweises für die Episodenliste.
        /// Eingeblendet wenn keine Episoden vorhanden sind.
        /// </summary>
        public Visibility EpisodesEmptyVisibility =>
            EpisodeList.Episodes.Count == 0 && !_isLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Lädt alle Episoden der angegebenen Serie samt Wiedergabestatus.
        /// Startet zusätzlich – sofern ein <see cref="BackgroundCoverService"/> injiziert ist –
        /// die Priorisierung der fehlenden Folgen-Cover dieser Serie und pausiert damit
        /// den Hintergrund-Scan, bis die sichtbare Liste versorgt ist.
        /// </summary>
        /// <param name="seriesId">ID der anzuzeigenden Serie.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Episodenliste samt Wiedergabestatus steht.</returns>
        public async Task LoadAsync(Guid seriesId)
        {
            // Priorität einer vorherigen Detailansicht sauber beenden, bevor wir neu starten.
            CancelPendingPriorityLoad();

            IsLoading = true;
            TrackList.Clear();

            try
            {
                SeriesDetailData data = await _tileBuilder.BuildAsync(
                    seriesId, episodeId => _ = PlayEpisodeAsync(episodeId), _lifecycleCts.Token);

                _seriesId = seriesId;
                Header.Apply(seriesId, data);
                EpisodeList.SetSource(data.Tiles);
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(EpisodesEmptyVisibility));
            }

            // Nach dem UI-Refresh: Priorität für die sichtbaren Folgen anstoßen.
            // Fire-and-forget — der Background-Service pausiert seinen Loop selbst,
            // damit die sichtbare Serie das HTTP-/Dateisystem-Kontingent zuerst bekommt.
            StartPriorityLoad(seriesId);
        }

        /// <summary>
        /// Startet die Wiedergabe aller Tracks der aktuell gewählten Episode.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Wiedergabe angestoßen ist.</returns>
        public Task PlaySelectedEpisodeAsync()
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("SeriesPlaySelectedEpisode");
            if (TrackList.SelectedEpisode is null)
            {
                return Task.CompletedTask;
            }

            return PlayEpisodeAsync(TrackList.SelectedEpisode.EpisodeId);
        }

        /// <summary>
        /// Markiert eine Episode als gehört. Erstellt oder aktualisiert den PlaybackState
        /// und lädt die Kachelliste neu, damit Haken und Fortschritt sofort stimmen.
        /// </summary>
        /// <param name="episodeId">ID der zu markierenden Episode.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Wiedergabestatus gespeichert und die Kachel aktualisiert ist.</returns>
        public async Task MarkAsPlayedAsync(Guid episodeId)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("SeriesMarkPlayed");
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IPlaybackStateDataService stateService =
                    scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                await stateService.MarkCompletedAsync(episodeId, _clock.UtcNow, _lifecycleCts.Token);
            }

            await LoadAsync(_seriesId);
        }

        /// <summary>
        /// Setzt den Wiedergabestatus einer Episode zurück (als ungehört markieren).
        /// Entfernt den gespeicherten PlaybackState und lädt die Kachelliste neu.
        /// </summary>
        /// <param name="episodeId">ID der zurückzusetzenden Episode.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Wiedergabestatus entfernt und die Kachel aktualisiert ist.</returns>
        public async Task MarkAsUnplayedAsync(Guid episodeId)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("SeriesMarkUnplayed");
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IPlaybackStateDataService stateService =
                    scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                await stateService.MarkNotStartedAsync(episodeId, _lifecycleCts.Token);
            }

            await LoadAsync(_seriesId);
        }

        /// <summary>
        /// Startet die Wiedergabe einer Episode.
        /// Lädt die lokalen Tracks und gibt sie an den <see cref="PlayerService"/> weiter.
        /// Existiert ein gespeicherter Fortschritt, wird die Wiedergabe dort fortgesetzt.
        /// </summary>
        /// <param name="episodeId">ID der abzuspielenden Episode.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Wiedergabe angestoßen ist.</returns>
        public async Task PlayEpisodeAsync(Guid episodeId)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("SeriesPlayEpisode");

            await PlaybackLauncher.PlayEpisodeAsync(_scopeFactory, _playerService, episodeId, _lifecycleCts.Token);
        }

        /// <summary>
        /// Bricht einen laufenden Priority-Cover-Load ab. Wird beim Verlassen der
        /// Detailseite aus dem <c>OnNavigatedFrom</c>-Pfad der Page aufgerufen, damit
        /// der Hintergrund-Loop nicht unnötig pausiert bleibt.
        /// </summary>
        public void CancelPendingPriorityLoad()
        {
            if (_priorityCts is null)
            {
                return;
            }

            try
            {
                _priorityCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS wurde bereits abgeräumt – defensiv schlucken.
            }

            _priorityCts.Dispose();
            _priorityCts = null;
        }

        /// <summary>
        /// Stoppt alle laufenden Service-Calls und Hintergrund-Aufgaben dieser
        /// Detail-VM-Instanz. Wird beim Verlassen der Page aufgerufen, damit
        /// pending DB-/Cover-Roundtrips nicht in einen verworfenen VM-State schreiben.
        /// </summary>
        public void Cleanup()
        {
            CancelPendingPriorityLoad();
            try
            {
                _lifecycleCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Bereits entsorgt — defensiv schlucken.
            }
        }

        /// <summary>
        /// Startet den Priority-Cover-Load für die geöffnete Serie im Hintergrund und
        /// legt ein neues <see cref="CancellationTokenSource"/> an, damit das VM bei
        /// Verlassen der Seite (<see cref="CancelPendingPriorityLoad"/>) sauber abbrechen kann.
        /// </summary>
        private void StartPriorityLoad(Guid seriesId)
        {
            if (_backgroundCoverService is null)
            {
                return;
            }

            _priorityCts = new CancellationTokenSource();
            CancellationToken token = _priorityCts.Token;
            _ = _backgroundCoverService.RequestPriorityForSeriesAsync(seriesId, token);
        }
    }
}
