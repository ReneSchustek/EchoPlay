using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Holt die Cover der Folgen-Kacheln nach: erst die sichtbare Charge, dann den Rest im
    /// Hintergrund.
    /// </summary>
    /// <remarks>
    /// Der Weg zu einem Cover hat drei Stufen (Datenbank, <c>cover.jpg</c> im Ordner,
    /// Kennzeichnung der ersten Spur) und dazu zwei Fäden — die Bilddaten kommen vom
    /// Hintergrund, das Bildobjekt darf nur auf dem Oberflächenfaden entstehen. Das ist eine
    /// eigene Aufgabe und gehört nicht in das Ansichtsmodell der Liste.
    /// </remarks>
    internal sealed class LocalEpisodeCoverLoader
    {
        /// <summary>
        /// Größe einer Nachlade-Charge. Sie erscheinen blockweise, was beim Blättern ruhiger
        /// wirkt als ein Rieseln einzelner Kacheln.
        /// </summary>
        private const int BatchSize = 60;

        /// <summary>
        /// Wie viele Cover gleichzeitig geladen werden. Genug für flüssiges Nachladen, ohne
        /// den Oberflächenfaden mit Änderungsmeldungen zu überschwemmen.
        /// </summary>
        private const int MaxParallelLoads = 8;

        /// <summary>
        /// Zielbreite der Kachelbilder. Die Kachel ist 120 Punkte breit; das Doppelte bleibt
        /// auf hoch aufgelösten Bildschirmen scharf und dekodiert trotzdem nur einen Bruchteil
        /// der 600 Punkte, die in der Datenbank liegen.
        /// </summary>
        private const int TileDecodeWidth = 240;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILocalCoverLoader _coverLoader;
        private readonly EchoPlay.App.Services.ICoverService? _coverService;
        private readonly ILogger? _logger;
        private readonly DispatcherQueue? _dispatcherQueue;

        /// <summary>
        /// Richtet das Nachladen ein.
        /// </summary>
        /// <param name="scopeFactory">Für den Zugriff auf die Spuren einer Folge.</param>
        /// <param name="coverLoader">Liest Cover aus Ordner und Kennzeichnung.</param>
        /// <param name="coverService">Liest Cover aus der Datenbank; ohne ihn entfällt die Stufe.</param>
        /// <param name="logger">Protokollkanal; darf fehlen.</param>
        /// <param name="dispatcherQueue">
        /// Der Oberflächenfaden. Fehlt er — etwa im Test —, entsteht das Bildobjekt direkt.
        /// </param>
        public LocalEpisodeCoverLoader(
            IServiceScopeFactory scopeFactory,
            ILocalCoverLoader coverLoader,
            EchoPlay.App.Services.ICoverService? coverService,
            ILogger? logger,
            DispatcherQueue? dispatcherQueue)
        {
            _scopeFactory = scopeFactory;
            _coverLoader = coverLoader;
            _coverService = coverService;
            _logger = logger;
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>
        /// Lädt das Cover einer einzelnen Kachel. Gedacht für die erste, sichtbare Charge —
        /// der Aufruf erfolgt bereits auf dem Oberflächenfaden, das Bildobjekt darf hier also
        /// unmittelbar entstehen.
        /// </summary>
        /// <param name="card">Die Kachel, deren Bild gesetzt wird.</param>
        /// <param name="episode">Die zugehörige Folge.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Einzel-Cover-Loader für die erste Kachel-Charge: IO-/TagLib-/Dekodier-Fehler dürfen die Kachel-Darstellung nicht stoppen; der Platzhalter bleibt stehen.")]
        public async Task LoadSingleAsync(LocalEpisodeCardViewModel card, Episode episode)
        {
            try
            {
                byte[]? bytes = await LoadCoverBytesAsync(episode);

                if (bytes is not null)
                {
                    card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes, TileDecodeWidth);
                }
            }
            catch
            {
                // Cover-Laden darf die Oberfläche nicht aufhalten — der Platzhalter bleibt stehen.
            }
        }

        /// <summary>
        /// Lädt die übrigen Cover in Chargen nach. Jede Charge wird fertig abgearbeitet, bevor
        /// die nächste beginnt; bei Abbruch — etwa einem Serienwechsel — endet es sofort.
        /// </summary>
        /// <param name="queue">Die noch offenen Kacheln mit ihren Folgen.</param>
        /// <param name="cancellationToken">Endet mit dem Verlassen der Serie.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Batch-Cover-Loader-Schleife: IO-/DB-/Dekodier-Fehler einzelner Episoden dürfen das Nachladen der restlichen Kacheln nicht stoppen; Fehler werden protokolliert.")]
        public async Task LoadRestAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> queue,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(queue);

            try
            {
                for (int offset = 0; offset < queue.Count; offset += BatchSize)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    int count = Math.Min(BatchSize, queue.Count - offset);
                    List<(LocalEpisodeCardViewModel Card, Episode Episode)> batch =
                        queue.GetRange(offset, count);

                    await LoadBatchAsync(batch, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch beim Seitenwechsel.
            }
            catch (Exception ex)
            {
                _logger?.Error("Hintergrund-Cover-Laden fehlgeschlagen", ex);
            }
        }

        /// <summary>
        /// Lädt eine Charge mit begrenzter Gleichzeitigkeit. Die Bilddaten kommen von
        /// Hintergrundfäden, das Bildobjekt entsteht auf dem Oberflächenfaden.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Parallele Cover-Lade-Tasks und UI-Thread-BitmapImage-Konvertierung: native COM-Fehler (SetSourceAsync) oder TagLib-Fehler dürfen weder die Task-Gruppe noch den UI-Thread reißen; ein Platzhalter bleibt stehen.")]
        private async Task LoadBatchAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> coverQueue,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim throttle = new(MaxParallelLoads);
            List<Task> tasks = new(coverQueue.Count);

            foreach ((LocalEpisodeCardViewModel card, Episode episode) in coverQueue)
            {
                if (cancellationToken.IsCancellationRequested) break;

                await throttle.WaitAsync(cancellationToken);

                // Die Bilddaten kommen vom Hintergrundfaden, weil File.Exists und die
                // Kennzeichnungs-Bibliothek blockierend arbeiten.
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        byte[]? bytes = await LoadCoverBytesAsync(episode);

                        if (bytes is not null && !cancellationToken.IsCancellationRequested)
                        {
                            await SetCoverAsync(card, bytes);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Serienwechsel — erwarteter Abbruch.
                    }
                    catch
                    {
                        // Cover-Laden darf die Oberfläche nicht aufhalten.
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
                // Serienwechsel — erwarteter Abbruch.
            }

            throttle.Dispose();
        }

        /// <summary>
        /// Setzt das Bild auf der Kachel — auf dem Oberflächenfaden, weil das Bildobjekt ein
        /// COM-Objekt ist. Ohne diesen Umweg scheitert es mit einem Fehler, den der
        /// umgebende Auffangblock schluckt, und die Kachel bliebe leer.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "BitmapImage-Erzeugung auf dem UI-Thread: native COM-Fehler aus SetSourceAsync dürfen den UI-Thread nicht reißen; die Kachel behält ihren Platzhalter.")]
        private async Task SetCoverAsync(LocalEpisodeCardViewModel card, byte[] bytes)
        {
            if (_dispatcherQueue is null)
            {
                // Tests ohne Oberflächenfaden — direkt setzen.
                card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes, TileDecodeWidth);
                return;
            }

            TaskCompletionSource tcs = new();

            // Fährt der Faden gerade herunter, kommt nichts mehr an. Ohne diese Prüfung bliebe
            // die Zusage unerfüllt und der Vorgang hinge für immer.
            bool enqueued = _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes, TileDecodeWidth);
                }
                catch
                {
                    // Fehler auf dem Oberflächenfaden — der Platzhalter bleibt stehen.
                }
                finally
                {
                    tcs.SetResult();
                }
            });

            if (!enqueued)
            {
                return;
            }

            await tcs.Task;
        }

        /// <summary>
        /// Holt die reinen Bilddaten einer Folge, ohne die Oberfläche anzufassen. Reihenfolge:
        /// Datenbank, <c>cover.jpg</c> im Ordner, Kennzeichnung der ersten Spur.
        /// </summary>
        /// <param name="episode">Die Folge, deren Cover gesucht wird.</param>
        /// <returns>Die Bilddaten, oder <see langword="null"/>, wenn es keines gibt.</returns>
        private async Task<byte[]?> LoadCoverBytesAsync(Episode episode)
        {
            if (_coverService is not null)
            {
                IReadOnlyDictionary<Guid, byte[]> coverMap =
                    await _coverService.GetEpisodeCoverBytesAsync([episode.Id]);
                if (coverMap.TryGetValue(episode.Id, out byte[]? dbBytes))
                {
                    return dbBytes;
                }
            }

            // Die erste Spur wird nur gesucht, wenn im Ordner kein Cover liegt — sonst läge
            // ein Datenbankzugriff je Kachel ohne Nutzen an.
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
    }
}
