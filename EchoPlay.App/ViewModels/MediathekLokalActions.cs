using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Interner Orchestrator für die Async-Aktionen der lokalen Mediathek.
    /// Enthält Serien-Laden, Episoden-/Track-Auswahl, Serien-Verwaltung (Löschen, Überwachung,
    /// Alle-Gehört), Fehlende-Folgen-Prüfung, Ordnerstruktur-Assistent und Cover-Verwaltung.
    /// Das Top-VM <see cref="MediathekLokalViewModel"/> hält nur noch Commands,
    /// Zustands-Properties und die Pass-Through-Schicht. Folgt dem Muster aus
    /// <see cref="MediathekOnlineActions"/> und <see cref="TagManagerActions"/>.
    /// </summary>
    internal sealed class MediathekLokalActions
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly StatusBarViewModel _statusBar;
        private readonly ICoverSearchService _coverSearchService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly IWatchToggleService? _watchToggleService;
        private readonly IFolderRestructureCoordinator? _restructureCoordinator;
        private readonly IMissingEpisodesCoordinator? _missingEpisodesCoordinator;
        private readonly IEpisodeCoverCoordinator? _coverCoordinator;
        private readonly IClock _clock;

        private readonly LocalLibraryScanViewModel _scanVM;
        private readonly LocalArtistsViewModel _artistsVM;
        private readonly LocalEpisodesViewModel _episodesVM;
        private readonly LocalTracksViewModel _tracksVM;

        /// <summary>
        /// Wird ausgelöst, nachdem fehlende Episoden ermittelt wurden.
        /// Die Page zeigt die übergebene Titelliste in einem ContentDialog an.
        /// Die Liste ist leer wenn alle Episoden lokal vorhanden sind.
        /// </summary>
        public event Action<IReadOnlyList<string>>? MissingEpisodesResolved;

        /// <summary>
        /// Wird ausgelöst, nachdem die Gesamtprüfung aller Serien abgeschlossen ist.
        /// Die Page zeigt den Bericht in einem Dialog an und bietet den TXT-Export an.
        /// </summary>
        public event Action<MissingEpisodesReport>? AllSeriesCheckCompleted;

        /// <summary>
        /// Wird ausgelöst, wenn der Ordnerstruktur-Assistent eine Vorschau erstellt hat.
        /// Die Page zeigt die geplanten Verschiebungen in einem ContentDialog an.
        /// Bei Bestätigung durch den Nutzer ruft die Page <see cref="ExecuteRestructureAsync"/> auf.
        /// </summary>
        public event Action<RestructurePreviewDisplay>? RestructurePreviewReady;

        /// <summary>
        /// Wird ausgelöst, bevor die Fehlende-Folgen-Prüfung beginnt.
        /// Die Page zeigt einen Drei-Optionen-Dialog (Online / Nur offline / Abbrechen)
        /// und liefert das Ergebnis als <see cref="MissingEpisodesMode"/>.
        /// </summary>
        public event Func<Task<MissingEpisodesMode>>? MissingEpisodesModeRequested;

        /// <summary>
        /// Initialisiert den Orchestrator mit allen Services und Sub-VMs.
        /// </summary>
        public MediathekLokalActions(
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            StatusBarViewModel statusBar,
            ICoverSearchService coverSearchService,
            IOnlineAccessGuard onlineAccessGuard,
            IWatchToggleService? watchToggleService,
            IFolderRestructureCoordinator? restructureCoordinator,
            IMissingEpisodesCoordinator? missingEpisodesCoordinator,
            IEpisodeCoverCoordinator? coverCoordinator,
            IClock clock,
            LocalLibraryScanViewModel scanVM,
            LocalArtistsViewModel artistsVM,
            LocalEpisodesViewModel episodesVM,
            LocalTracksViewModel tracksVM)
        {
            _scopeFactory              = scopeFactory;
            _confirmationDialogService = confirmationDialogService;
            _statusBar                 = statusBar;
            _coverSearchService        = coverSearchService;
            _onlineAccessGuard         = onlineAccessGuard;
            _watchToggleService        = watchToggleService;
            _restructureCoordinator    = restructureCoordinator;
            _missingEpisodesCoordinator = missingEpisodesCoordinator;
            _coverCoordinator          = coverCoordinator;
            _clock                     = clock;

            _scanVM     = scanVM;
            _artistsVM  = artistsVM;
            _episodesVM = episodesVM;
            _tracksVM   = tracksVM;
        }

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt Bibliothekseinstellungen und alle Serien mit lokalem Ordner. Setzt die
        /// Auswahl zurück – mittlere und rechte Spalte werden geleert. Die Künstler-Liste
        /// und das Cover-Laden übernimmt das <see cref="LocalArtistsViewModel"/>; der Orchestrator
        /// koordiniert nur die AppSettings-Spiegelung in den ScanVM.
        /// </summary>
        public async Task LoadAsync()
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
                _scanVM.LibraryRootPath         = settings.LocalLibraryRootPath ?? string.Empty;
                _scanVM.NeedsLibraryFolderSetup = string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath);
            }

            _episodesVM.Clear();
            _tracksVM.Clear();
            await _artistsVM.LoadFromDatabaseAsync();
        }

        // ── Auswahl-Logik ────────────────────────────────────────────────────────

        /// <summary>
        /// Wählt eine Serie aus und lädt deren Episoden mit lokalem Ordner in die mittlere Spalte.
        /// Die rechte Spalte wird dabei geleert. Der Orchestrator koordiniert die drei Sub-VMs.
        /// </summary>
        /// <param name="artist">Die ausgewählte Serie.</param>
        public async Task SelectArtistAsync(LocalArtistCardViewModel artist)
        {
            _artistsVM.SelectArtist(artist);
            _tracksVM.Clear();

            // Episoden und Wiedergabestatus laden
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(artist.SeriesId);

            List<Guid> episodeIds = [.. episodes
                .Where(e => e.LocalFolderPath is not null)
                .Select(e => e.Id)];

            HashSet<Guid> completedIds = await stateService.GetCompletedEpisodeIdsAsync(episodeIds);

            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();

            HashSet<Guid> inProgressIds = allStates
                .Where(s => episodeIds.Contains(s.EpisodeId) && !s.IsCompleted && s.LastPosition > TimeSpan.Zero)
                .Select(s => s.EpisodeId)
                .ToHashSet();

            await _episodesVM.LoadForSeriesAsync(artist, episodes, completedIds, inProgressIds);
        }

        /// <summary>
        /// Wählt eine Episode aus und lädt deren Tracks in die rechte Spalte.
        /// </summary>
        /// <param name="episode">Die ausgewählte Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SelectEpisodeAsync(LocalEpisodeCardViewModel episode)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.EpisodeId);

            _tracksVM.SetTracks(episode, tracks);
        }

        // ── Serien-Verwaltung ─────────────────────────────────────────────────────

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer lokalen Serie um und aktualisiert die Karte.
        /// Die eigentliche Logik liegt im <see cref="IWatchToggleService"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="watch">Neuer Status.</param>
        public async Task ToggleWatchAsync(Guid seriesId, bool watch)
        {
            if (_watchToggleService is null)
            {
                return;
            }

            await _watchToggleService.ToggleAsync(seriesId, watch);

            LocalArtistCardViewModel? card = _artistsVM.AllArtists
                .FirstOrDefault(a => a.SeriesId == seriesId);
            if (card is not null)
            {
                card.IsWatched = watch;
            }
        }

        /// <summary>
        /// Kennzeichnet alle Episoden einer Serie als vollständig gehört.
        /// Existiert für eine Episode bereits ein <see cref="PlaybackState"/>, wird nur
        /// <c>IsCompleted</c> gesetzt. Fehlt der Eintrag, wird er neu angelegt.
        /// Statistiken in der Statusleiste werden nach dem Vorgang aktualisiert.
        /// </summary>
        /// <param name="seriesId">ID der betroffenen Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task MarkAllAsReadAsync(Guid seriesId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService       = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);

            foreach (Episode episode in episodes)
            {
                PlaybackState? existing = await playbackService.GetByEpisodeIdAsync(episode.Id);

                if (existing is not null)
                {
                    if (!existing.IsCompleted)
                    {
                        existing.IsCompleted = true;
                        existing.CompletedAt = _clock.UtcNow;
                        await playbackService.UpdateAsync(existing);
                    }
                }
                else
                {
                    await playbackService.AddAsync(new PlaybackState
                    {
                        EpisodeId    = episode.Id,
                        IsCompleted  = true,
                        CompletedAt  = _clock.UtcNow,
                        LastPlayedAt = _clock.UtcNow
                    });
                }
            }

            // Statistiken (gehörte/offene Folgen) in der Statusleiste aktualisieren
            await _statusBar.RefreshAsync();
        }

        /// <summary>
        /// Entfernt eine Serie aus der Bibliothek (Soft-Delete in der DB).
        /// Die Dateien auf der Festplatte bleiben erhalten – beim nächsten Scan
        /// wird die Serie wieder erkannt und kann erneut importiert werden.
        /// </summary>
        /// <param name="seriesId">ID der zu entfernenden Serie.</param>
        public async Task DeleteSeriesFromLibraryAsync(Guid seriesId)
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Aus Bibliothek entfernen",
                "Die Serie und alle zugehörigen Episoden werden aus der Bibliothek entfernt. " +
                "Die Dateien auf der Festplatte bleiben erhalten. Fortfahren?");

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            await LoadAsync();
        }

        /// <summary>
        /// Löscht eine Serie unwiderruflich – sowohl aus der DB als auch von der Festplatte.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="folderPath">Lokaler Ordnerpfad der Serie.</param>
        public async Task DeleteSeriesFromDiskAsync(Guid seriesId, string? folderPath)
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Von Festplatte löschen",
                "Die Serie, alle Episoden und alle Audiodateien werden unwiderruflich gelöscht. " +
                "Dieser Vorgang kann nicht rückgängig gemacht werden. Fortfahren?");

            if (!confirmed)
            {
                return;
            }

            // Erst aus DB löschen
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            // Dann Ordner auf der Festplatte löschen
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                await Task.Run(() => Directory.Delete(folderPath, recursive: true));
            }

            await LoadAsync();
        }

        // ── Fehlende Folgen ────────────────────────────────────────────────────────

        /// <summary>
        /// Ermittelt fehlende Folgen einer Serie. Fragt den Nutzer zuerst über
        /// <see cref="MissingEpisodesModeRequested"/>, ob online geprüft werden soll, und
        /// delegiert die eigentliche Analyse an den <see cref="IMissingEpisodesCoordinator"/>.
        /// </summary>
        /// <param name="seriesId">ID der zu prüfenden Serie.</param>
        public async Task ShowMissingEpisodesAsync(Guid seriesId)
        {
            if (_missingEpisodesCoordinator is null)
            {
                return;
            }

            LocalArtistCardViewModel? card = _artistsVM.AllArtists.FirstOrDefault(a => a.SeriesId == seriesId);

            MissingEpisodesMode mode = await RequestMissingEpisodesModeAsync();
            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            IReadOnlyList<string> result = await _missingEpisodesCoordinator.CheckSingleSeriesAsync(
                seriesId, card?.LocalFolderPath, mode);

            MissingEpisodesResolved?.Invoke(result);
        }

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen.
        /// Delegiert an den <see cref="IMissingEpisodesCoordinator"/> und feuert
        /// <see cref="AllSeriesCheckCompleted"/> mit dem Ergebnis.
        /// </summary>
        public async Task CheckAllSeriesAsync()
        {
            if (_missingEpisodesCoordinator is null)
            {
                return;
            }

            MissingEpisodesMode mode = await RequestMissingEpisodesModeAsync();
            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            MissingEpisodesReport report = await _missingEpisodesCoordinator.CheckAllSeriesAsync(mode);
            AllSeriesCheckCompleted?.Invoke(report);
        }

        /// <summary>
        /// Holt den Modus aus dem UI-Dialog. Ohne Listener wird "Nur offline" angenommen –
        /// das ist das Verhalten in Unit-Tests, in denen kein Dialog existiert.
        /// </summary>
        private async Task<MissingEpisodesMode> RequestMissingEpisodesModeAsync()
        {
            if (MissingEpisodesModeRequested is null)
            {
                return MissingEpisodesMode.OfflineOnly;
            }

            return await MissingEpisodesModeRequested.Invoke();
        }

        // ── Ordnerstruktur-Assistent ─────────────────────────────────────────────

        /// <summary>
        /// Analysiert den Serienordner und erstellt eine Vorschau für den Ordnerstruktur-Umbau.
        /// Löst <see cref="RestructurePreviewReady"/> aus, wenn verschiebbare Dateien gefunden wurden.
        /// Die eigentliche Analyse läuft im <see cref="IFolderRestructureCoordinator"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Ordner analysiert werden soll.</param>
        public async Task AnalyzeRestructureAsync(Guid seriesId)
        {
            if (_restructureCoordinator is null)
            {
                return;
            }

            LocalArtistCardViewModel? card = _artistsVM.AllArtists.FirstOrDefault(a => a.SeriesId == seriesId);
            if (card?.LocalFolderPath is null)
            {
                return;
            }

            RestructurePreviewDisplay? preview =
                await _restructureCoordinator.AnalyzeAsync(card.LocalFolderPath);

            if (preview is null)
            {
                return;
            }

            RestructurePreviewReady?.Invoke(preview);
        }

        /// <summary>
        /// Führt den Ordnerstruktur-Umbau aus und löst danach einen Neu-Scan der Bibliothek aus.
        /// Delegiert an den <see cref="IFolderRestructureCoordinator"/>.
        /// </summary>
        /// <param name="preview">Die zuvor erstellte App-Display-Vorschau.</param>
        /// <returns>Anzahl der verschobenen Dateien.</returns>
        public async Task<int> ExecuteRestructureAsync(RestructurePreviewDisplay preview)
        {
            if (_restructureCoordinator is null)
            {
                return 0;
            }

            return await _restructureCoordinator.ExecuteAsync(preview);
        }

        // ── Cover-Verwaltung ────────────────────────────────────────────────────

        /// <summary>
        /// Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.
        /// Muss von der Page aufgerufen werden, bevor der Cover-Such-Dialog geöffnet wird.
        /// </summary>
        public Task<IDisposable?> RequestOnlineAccessForCoverSearchAsync()
            => _onlineAccessGuard.RequestOnlineAccessAsync();

        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Begriff. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(string query, CancellationToken ct)
        {
            if (_coverCoordinator is null)
            {
                return Task.FromResult<IReadOnlyList<CoverSearchHit>>([]);
            }

            return _coverCoordinator.SearchCoversAsync(query, ct);
        }

        /// <summary>
        /// Übernimmt rohe Bytes als Serien-Cover. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySeriesCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Übernimmt rohe Bytes als Episoden-Cover. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplyEpisodeCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Serien-Cover.
        /// Delegiert an den <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySelectedSeriesCoverAsync(card, hit);
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Episoden-Cover.
        /// Delegiert an den <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySelectedEpisodeCoverAsync(card, hit);
        }
    }
}
