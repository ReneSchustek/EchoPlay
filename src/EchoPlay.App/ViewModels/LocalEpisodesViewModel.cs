using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Episoden-Spalte der lokalen Mediathek.
    /// Verwaltet Episodenliste, Filter, Sortierung, Sonderfolgen-Tab, Cover-Ladevorgänge und
    /// den Gehört-/Ungehört-Status einzelner Folgen. Wird vom <see cref="MediathekLokalViewModel"/>
    /// als Pass-Through-Ziel eingebunden, damit bestehende XAML-Bindings unverändert funktionieren.
    /// </summary>
    public sealed class LocalEpisodesViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILocalCoverLoader _coverLoader;
        private readonly EchoPlay.App.Services.CoverService? _coverService;
        private readonly IClock _clock;
        private readonly ILogger? _logger;

        /// <summary>
        /// DispatcherQueue des UI-Threads – wird im Konstruktor auf dem UI-Thread erfasst und
        /// später genutzt, um Hintergrund-Cover-Ladevorgänge auf den UI-Thread zu marshallen.
        /// In Unit-Tests ohne WinUI-3-Dispatcher bleibt das Feld <see langword="null"/>.
        /// </summary>
        private readonly DispatcherQueue? _dispatcherQueue;

        private IReadOnlyList<LocalEpisodeCardViewModel> _episodes = [];
        private List<LocalEpisodeCardViewModel> _allEpisodes = [];
        private HashSet<Guid> _completedEpisodeIds = [];
        private HashSet<Guid> _inProgressEpisodeIds = [];
        private CancellationTokenSource? _coverCts;

        private int _episodeSortIndex;
        private int _episodeFilterIndex;
        private int _episodeTabIndex;

        /// <summary>
        /// Initialisiert das Sub-ViewModel mit den für Episoden benötigten Diensten.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankzugriffe beim Setzen/Entfernen des Wiedergabestatus und beim Cover-Fallback.</param>
        /// <param name="coverLoader">Lädt Cover-Bilder aus dem lokalen Dateisystem (cover.jpg oder ID3-Tag).</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. In Tests <see langword="null"/>.</param>
        /// <param name="logger">Logger für Hintergrund-Fehler wie fehlgeschlagenes Cover-Laden. In Tests <see langword="null"/>.</param>
        public LocalEpisodesViewModel(
            IServiceScopeFactory scopeFactory,
            ILocalCoverLoader coverLoader,
            IClock clock,
            EchoPlay.App.Services.CoverService? coverService = null,
            ILogger? logger = null)
        {
            _scopeFactory = scopeFactory;
            _coverLoader = coverLoader;
            _clock = clock;
            _coverService = coverService;
            _logger = logger;

            // DispatcherQueue auf dem UI-Thread erfassen – notwendig für das Marshalling der
            // BitmapImage-Erzeugung. In Tests existiert kein WinUI-3-Dispatcher, dann bleibt das Feld null.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                _dispatcherQueue = null;
            }
        }

        // ── Episoden-Daten ───────────────────────────────────────────────────────

        /// <summary>
        /// Gefilterte und sortierte Episodenliste der aktuell gewählten Serie.
        /// Leer, solange keine Serie gewählt ist.
        /// </summary>
        public IReadOnlyList<LocalEpisodeCardViewModel> Episodes
        {
            get => _episodes;
            private set
            {
                if (SetProperty(ref _episodes, value))
                {
                    OnPropertyChanged(nameof(EpisodesEmptyVisibility));
                    OnPropertyChanged(nameof(EpisodesLoadedVisibility));
                }
            }
        }

        /// <summary>
        /// Aktuell gewählte Sortierung der Episodenliste (0 = Nummer, 1 = Nummer absteigend, 2 = Titel).
        /// Eine Änderung sortiert die geladene Episode-Liste sofort neu.
        /// </summary>
        public int EpisodeSortIndex
        {
            get => _episodeSortIndex;
            set
            {
                if (SetProperty(ref _episodeSortIndex, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// Aktuell gewählter Filter der Episodenliste.
        /// 0 = Alle, 1 = Ungehört, 2 = Gehört, 3 = Angefangen.
        /// Eine Änderung filtert und sortiert die geladene Episode-Liste sofort neu.
        /// </summary>
        public int EpisodeFilterIndex
        {
            get => _episodeFilterIndex;
            set
            {
                if (SetProperty(ref _episodeFilterIndex, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// Aktiver Tab: 0 = Folgen (regulär), 1 = Sonderfolgen.
        /// Steuert welche Episoden in der mittleren Spalte angezeigt werden.
        /// </summary>
        public int EpisodeTabIndex
        {
            get => _episodeTabIndex;
            set
            {
                if (SetProperty(ref _episodeTabIndex, value))
                {
                    ApplyFilterAndSort();
                    OnPropertyChanged(nameof(HasSpecialEpisodes));
                }
            }
        }

        /// <summary>
        /// True wenn die aktuelle Serie Sonderfolgen hat – nur dann wird der Tab sichtbar.
        /// </summary>
        public bool HasSpecialEpisodes => _allEpisodes.Any(e => e.IsSpecialEpisode);

        /// <summary>
        /// Anzahl der Sonderfolgen für die Tab-Beschriftung (z.B. "Sonderfolgen (35)").
        /// </summary>
        public int SpecialEpisodeCount => _allEpisodes.Count(e => e.IsSpecialEpisode);

        /// <summary>
        /// Sichtbarkeit des "Folge wählen"-Platzhalters in der mittleren Spalte.
        /// Erscheint solange keine Episoden geladen sind.
        /// </summary>
        public Visibility EpisodesEmptyVisibility =>
            _episodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Sortier-ComboBox in der mittleren Spalte.
        /// Nur eingeblendet wenn Episoden geladen sind.
        /// </summary>
        public Visibility EpisodesLoadedVisibility =>
            _episodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // ── Laden / Zurücksetzen ─────────────────────────────────────────────────

        /// <summary>
        /// Lädt die Episoden einer Serie, setzt Filter/Sortierung/Tab zurück und startet
        /// den Cover-Ladevorgang. Vorherige Cover-Tasks werden dabei abgebrochen.
        /// Die Wiedergabestatus-IDs werden vom Aufrufer übergeben, weil sie aus der DB kommen
        /// und im Aufrufer bereits per Batch-Query geladen werden.
        /// </summary>
        /// <param name="artist">Die ausgewählte Serie – wird nur für das Kürzen des Serien-Präfixes verwendet.</param>
        /// <param name="episodes">Alle Episoden der Serie aus der Datenbank.</param>
        /// <param name="completedIds">IDs aller vollständig gehörten Episoden.</param>
        /// <param name="inProgressIds">IDs aller begonnenen, aber nicht abgeschlossenen Episoden.</param>
        public async Task LoadForSeriesAsync(
            LocalArtistCardViewModel artist,
            IReadOnlyList<Episode> episodes,
            HashSet<Guid> completedIds,
            HashSet<Guid> inProgressIds)
        {
            ArgumentNullException.ThrowIfNull(artist);
            // Laufende Cover-Tasks der vorherigen Serie abbrechen – verhindert,
            // dass alte Hintergrund-Tasks Cover auf bereits ersetzte Karten setzen.
            if (_coverCts is not null)
            {
                await _coverCts.CancelAsync();
                _coverCts.Dispose();
            }
            _coverCts = new CancellationTokenSource();
            CancellationToken coverToken = _coverCts.Token;

            // Schritt 1: Karten OHNE Cover erstellen – GridView rendert sofort
            List<LocalEpisodeCardViewModel> episodeCards = [];
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> coverQueue = [];

            foreach (Episode episode in episodes.Where(e => e.LocalFolderPath is not null).OrderBy(e => e.EpisodeNumber))
            {
                // Serien-Präfix aus dem Episodentitel entfernen, falls vorhanden.
                // Viele Hörspiel-Ordner heißen z.B. "Abenteurer unserer Zeit - Der Pirat" –
                // der Serienname am Anfang ist redundant und verschwendet Platz in der Kachel.
                string displayTitle = StripSeriesPrefix(episode.Title, artist.Title);

                // Sonderfolge: Nummer 0 oder null (kein erkennbares Nummernmuster)
                bool isSpecial = episode.EpisodeNumber is null or 0;

                LocalEpisodeCardViewModel card = new(
                    episodeId: episode.Id,
                    episodeNumber: episode.EpisodeNumber,
                    title: displayTitle,
                    localTrackCount: episode.LocalTrackCount ?? 0,
                    folderPath: episode.LocalFolderPath,
                    isSpecialEpisode: isSpecial);

                episodeCards.Add(card);
                coverQueue.Add((card, episode));
            }

            _completedEpisodeIds = completedIds;
            _inProgressEpisodeIds = inProgressIds;

            // Gehört-Status auf den Karten setzen (nach dem Laden der IDs)
            foreach (LocalEpisodeCardViewModel card in episodeCards)
            {
                card.IsCompleted = _completedEpisodeIds.Contains(card.EpisodeId);
            }

            // Ungefilterte Gesamtliste speichern, Tab/Filter/Sortierung zurücksetzen
            _allEpisodes = episodeCards;
            _episodeTabIndex = 0;
            _episodeFilterIndex = 0;
            OnPropertyChanged(nameof(EpisodeTabIndex));
            OnPropertyChanged(nameof(EpisodeFilterIndex));
            OnPropertyChanged(nameof(HasSpecialEpisodes));
            OnPropertyChanged(nameof(SpecialEpisodeCount));
            ApplyFilterAndSort();

            // Erste Charge: max. 60 Kacheln (reguläre + Sonderfolgen gemischt) MIT Cover laden,
            // damit der aktive Tab sofort Cover zeigt. Sonderfolgen werden nicht mehr verzögert,
            // weil die Cover-Dateien auf lokaler SSD schnell genug laden.
            int firstBatchSize = Math.Min(60, coverQueue.Count);

            for (int i = 0; i < firstBatchSize; i++)
            {
                if (coverToken.IsCancellationRequested) return;
                await LoadCoverForEpisodeCardAsync(coverQueue[i].Card, coverQueue[i].Episode);
            }

            // Rest im Hintergrund – ab Position 60, in 60er-Chargen nachladend.
            if (coverQueue.Count > firstBatchSize)
            {
                List<(LocalEpisodeCardViewModel Card, Episode Episode)> remaining =
                    coverQueue.GetRange(firstBatchSize, coverQueue.Count - firstBatchSize);
                _ = LoadCoversBatchedAsync(remaining, coverToken);
            }
        }

        /// <summary>
        /// Leert die Episodenliste und bricht laufende Cover-Tasks ab.
        /// Wird aufgerufen, wenn die Serienauswahl zurückgesetzt wird oder ein neuer Scan startet.
        /// </summary>
        public void Clear()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;

            _allEpisodes = [];
            _completedEpisodeIds = [];
            _inProgressEpisodeIds = [];
            Episodes = [];
            OnPropertyChanged(nameof(HasSpecialEpisodes));
            OnPropertyChanged(nameof(SpecialEpisodeCount));
        }

        // ── Filterung / Sortierung ───────────────────────────────────────────────

        /// <summary>
        /// Filtert und sortiert die Episodenliste basierend auf dem gewählten Filter und Sortierkriterium.
        /// Arbeitet rein im Speicher auf <see cref="_allEpisodes"/> – kein DB-Zugriff nötig.
        /// </summary>
        public void ApplyFilterAndSort()
        {
            // Schritt 0: Tab-Filter – reguläre Folgen vs. Sonderfolgen
            IEnumerable<LocalEpisodeCardViewModel> tabFiltered = _episodeTabIndex == 1
                ? _allEpisodes.Where(e => e.IsSpecialEpisode)
                : _allEpisodes.Where(e => !e.IsSpecialEpisode);

            // Schritt 1: Status-Filter
            IEnumerable<LocalEpisodeCardViewModel> filtered = _episodeFilterIndex switch
            {
                1 => tabFiltered.Where(e => !_completedEpisodeIds.Contains(e.EpisodeId)
                                             && !_inProgressEpisodeIds.Contains(e.EpisodeId)),
                2 => tabFiltered.Where(e => _completedEpisodeIds.Contains(e.EpisodeId)),
                3 => tabFiltered.Where(e => _inProgressEpisodeIds.Contains(e.EpisodeId)),
                _ => tabFiltered
            };

            // Schritt 2: Sortieren
            IEnumerable<LocalEpisodeCardViewModel> sorted = _episodeSortIndex switch
            {
                1 => filtered.OrderByDescending(e => e.EpisodeNumber ?? int.MaxValue),
                2 => filtered.OrderBy(e => e.Title),
                _ => filtered.OrderBy(e => e.EpisodeNumber ?? int.MaxValue)
            };

            _episodes = sorted.ToList();
            OnPropertyChanged(nameof(Episodes));
            OnPropertyChanged(nameof(EpisodesEmptyVisibility));
            OnPropertyChanged(nameof(EpisodesLoadedVisibility));
        }

        // ── Cover-Laden ──────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt Cover in 60er-Chargen sequenziell nach. Jede Charge wird komplett abgearbeitet
        /// bevor die nächste startet. Erzeugt einen Infinite-Scrolling-Effekt: Cover erscheinen blockweise.
        /// Bei Abbruch (Serienwechsel) wird sofort aufgehört.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Batch-Cover-Loader-Schleife: IO-/DB-/Dekodier-Fehler einzelner Episoden dürfen das Nachladen der restlichen Kacheln nicht stoppen; Fehler werden in Trace geschrieben.")]
        private async Task LoadCoversBatchedAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> queue,
            CancellationToken cancellationToken)
        {
            try
            {
                int batchSize = 60;

                for (int offset = 0; offset < queue.Count; offset += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    int count = Math.Min(batchSize, queue.Count - offset);
                    List<(LocalEpisodeCardViewModel Card, Episode Episode)> batch =
                        queue.GetRange(offset, count);

                    await LoadCoversThrottledAsync(batch, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch bei Seitenwechsel
            }
            catch (Exception ex)
            {
                _logger?.Error("Hintergrund-Cover-Laden fehlgeschlagen", ex);
            }
        }

        /// <summary>
        /// Lädt die Cover aller Episodenkarten mit begrenzter Parallelität.
        /// Die Byte-Daten werden auf Hintergrundthreads geladen (IO-bound),
        /// die BitmapImage-Erstellung erfolgt auf dem UI-Thread (WinRT-COM-Pflicht).
        /// Maximal 8 Cover werden gleichzeitig geladen, damit der UI-Thread nicht
        /// mit hunderten PropertyChanged-Notifications gleichzeitig überflutet wird.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Parallele Cover-Lade-Tasks und UI-Thread-BitmapImage-Konvertierung: native COM-Fehler (SetSourceAsync) oder TagLib-Fehler dürfen weder die Task-Gruppe noch den UI-Thread reißen; ein Platzhalter bleibt stehen.")]
        private async Task LoadCoversThrottledAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> coverQueue,
            CancellationToken cancellationToken)
        {
            // 8 parallele Ladevorgänge – genug für flüssiges Nachladen, ohne den UI-Thread zu überlasten
            SemaphoreSlim throttle = new(8);
            List<Task> tasks = new(coverQueue.Count);

            foreach ((LocalEpisodeCardViewModel card, Episode episode) in coverQueue)
            {
                if (cancellationToken.IsCancellationRequested) break;

                await throttle.WaitAsync(cancellationToken);

                // Byte-Laden auf Hintergrundthread, BitmapImage-Erstellung danach auf UI-Thread.
                // Task.Run ist nötig, weil File.Exists und TagLib# synchron blockieren würden.
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        byte[]? bytes = await LoadCoverBytesAsync(episode);

                        if (bytes is not null && !cancellationToken.IsCancellationRequested)
                        {
                            // BitmapImage ist ein WinRT-COM-Objekt – Erstellung nur auf dem UI-Thread erlaubt.
                            // Ohne dieses Marshalling schlägt SetSourceAsync mit einer COM-Exception fehl,
                            // die der catch-Block verschluckt und das Cover bleibt ein Platzhalter.
                            if (_dispatcherQueue is not null)
                            {
                                TaskCompletionSource tcs = new();

                                // TryEnqueue gibt false zurück wenn der Dispatcher herunterfährt –
                                // ohne Prüfung würde die TaskCompletionSource nie abgeschlossen
                                // und der Task hängt ewig.
                                bool enqueued = _dispatcherQueue.TryEnqueue(async () =>
                                {
                                    try
                                    {
                                        card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                                    }
                                    catch
                                    {
                                        // Cover-Fehler auf dem UI-Thread – Platzhalter bleibt stehen
                                    }
                                    finally
                                    {
                                        tcs.SetResult();
                                    }
                                });

                                if (!enqueued)
                                {
                                    // Dispatcher fährt runter – Cover-Laden abbrechen
                                    return;
                                }

                                await tcs.Task;
                            }
                            else
                            {
                                // Unit-Tests ohne Dispatcher – direkt setzen
                                card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Serienwechsel – erwarteter Abbruch
                    }
                    catch
                    {
                        // Cover-Laden darf die UI nicht blockieren – Platzhalter bleibt stehen
                    }
                    finally
                    {
                        _ = throttle.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Serienwechsel – erwarteter Abbruch
            }

            throttle.Dispose();
        }

        /// <summary>
        /// Lädt die rohen Cover-Bytes einer Episode (threadpool-safe, kein UI-Zugriff).
        /// Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag des ersten Tracks.
        /// </summary>
        /// <param name="episode">Die Episode, deren Cover geladen werden soll.</param>
        /// <returns>Rohe Bilddaten oder <see langword="null"/> wenn kein Cover vorhanden.</returns>
        private async Task<byte[]?> LoadCoverBytesAsync(Episode episode)
        {
            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                IReadOnlyDictionary<Guid, byte[]> coverMap =
                    await _coverService.GetEpisodeCoverBytesAsync([episode.Id]);
                if (coverMap.TryGetValue(episode.Id, out byte[]? dbBytes))
                {
                    return dbBytes;
                }
            }

            // Ersten Track für den ID3-Fallback ermitteln – nur wenn cover.jpg fehlt
            string? firstTrackPath = null;

            if (episode.LocalFolderPath is not null &&
                !File.Exists(Path.Combine(episode.LocalFolderPath, Core.CoverConstants.CoverFileName)))
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ILocalTrackDataService trackService = scope.ServiceProvider
                    .GetRequiredService<ILocalTrackDataService>();

                IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id);
                firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
            }

            return await _coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
        }

        /// <summary>
        /// Lädt das Cover einer Episode asynchron und setzt es auf der Karte.
        /// Wird für die erste Charge (max. 60 Kacheln) direkt auf dem UI-Thread aufgerufen,
        /// daher ist BitmapImage-Erstellung hier sicher.
        /// Fehler werden still ignoriert – fehlende Cover sind kein kritisches Problem.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Einzel-Cover-Loader für die erste Kachel-Charge: IO-/TagLib-/Dekodier-Fehler dürfen die Kachel-Darstellung nicht stoppen; der Platzhalter bleibt stehen.")]
        private async Task LoadCoverForEpisodeCardAsync(LocalEpisodeCardViewModel card, Episode episode)
        {
            try
            {
                byte[]? bytes = await LoadCoverBytesAsync(episode);

                if (bytes is not null)
                {
                    card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                }
            }
            catch
            {
                // Cover-Laden darf die UI nicht blockieren – fehlende Cover zeigen den Platzhalter
            }
        }

        // ── Episoden-Status ──────────────────────────────────────────────────────

        /// <summary>
        /// Markiert eine Episode als vollständig gehört.
        /// Setzt <c>IsCompleted = true</c> und <c>CompletedAt = DateTime.UtcNow</c> im PlaybackState.
        /// Legt einen neuen PlaybackState an, falls noch keiner existiert.
        /// </summary>
        /// <param name="episodeId">Die ID der zu markierenden Episode.</param>
        public async Task MarkEpisodeAsPlayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            await stateService.MarkCompletedAsync(episodeId, _clock.UtcNow);

            // Kachel sofort aktualisieren – ohne Serienwechsel und Rückkehr.
            // Haken erscheint per PropertyChanged auf IsCompleted → CompletedCheckVisibility.
            _ = _completedEpisodeIds.Add(episodeId);
            _ = _inProgressEpisodeIds.Remove(episodeId);
            LocalEpisodeCardViewModel? card = _allEpisodes.FirstOrDefault(c => c.EpisodeId == episodeId);
            if (card is not null)
            {
                card.IsCompleted = true;
            }
        }

        /// <summary>
        /// Markiert eine Episode als ungehört.
        /// Löscht den PlaybackState vollständig aus der Datenbank.
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        public async Task MarkEpisodeAsUnplayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            await stateService.MarkNotStartedAsync(episodeId);

            // Kachel sofort aktualisieren – Haken verschwindet per PropertyChanged.
            _ = _completedEpisodeIds.Remove(episodeId);
            _ = _inProgressEpisodeIds.Remove(episodeId);
            LocalEpisodeCardViewModel? card = _allEpisodes.FirstOrDefault(c => c.EpisodeId == episodeId);
            if (card is not null)
            {
                card.IsCompleted = false;
            }
        }

        // ── Hilfsmethoden ────────────────────────────────────────────────────────

        /// <summary>
        /// Entfernt den Seriennamen als Präfix aus dem Episodentitel, falls vorhanden.
        /// Viele Hörspiel-Ordner tragen den Seriennamen als Präfix, z.B.
        /// "Abenteurer unserer Zeit - Der Pirat" → "Der Pirat".
        /// Trennzeichen wie " - ", "- ", " -", "-" werden ebenfalls bereinigt.
        /// </summary>
        /// <param name="episodeTitle">Originaler Episodentitel (typischerweise der Ordnername).</param>
        /// <param name="seriesTitle">Name der übergeordneten Serie.</param>
        /// <returns>
        /// Bereinigter Titel ohne Serien-Präfix. Wenn der Titel nach dem Entfernen leer wäre,
        /// wird der Originaltitel zurückgegeben – ein leerer Anzeigename ist nie sinnvoll.
        /// </returns>
        private static string StripSeriesPrefix(string episodeTitle, string seriesTitle)
        {
            if (string.IsNullOrWhiteSpace(episodeTitle) || string.IsNullOrWhiteSpace(seriesTitle))
            {
                return episodeTitle;
            }

            // Prüfen ob der Episodentitel mit dem Seriennamen beginnt (Groß-/Kleinschreibung ignorieren)
            if (!episodeTitle.StartsWith(seriesTitle, StringComparison.OrdinalIgnoreCase))
            {
                return episodeTitle;
            }

            // Serien-Präfix entfernen
            string remaining = episodeTitle[seriesTitle.Length..];

            // Führende Trennzeichen und Leerzeichen bereinigen:
            // " - ", "- ", " -", "-", "_", "–" (Halbgeviertstrich)
            remaining = remaining.TrimStart(' ', '-', '_', '\u2013');
            remaining = remaining.TrimStart();

            // Sicherheitsprüfung: wenn nach dem Bereinigen nichts übrig bleibt,
            // den Originaltitel behalten – ein leerer Titel ist nie hilfreich
            return remaining.Length > 0 ? remaining : episodeTitle;
        }

        /// <summary>
        /// Gibt die Cover-CancellationTokenSource frei und bricht laufende Cover-Tasks ab.
        /// </summary>
        public void Dispose()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }
    }
}
