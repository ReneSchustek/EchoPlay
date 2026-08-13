using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MicrosoftDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Beschafft die Cover für die Kacheln der Startseite.
    /// </summary>
    /// <remarks>
    /// Der Startpfad ist bewusst eng: Er liest ausschließlich aus der Datenbank, und zwar
    /// alle Folgen-Cover eines Durchgangs in einer einzigen Abfrage. Was dort fehlt, zeigt
    /// zunächst das Serien-Cover; die betroffenen Kacheln werden vorgemerkt und bekommen ihr
    /// eigenes Bild nachgereicht, sobald der Hintergrunddienst es aus Dateisystem, Kennzeichnung
    /// oder Anbieter geholt hat. Ohne diese Trennung würde die Startseite auf Dateizugriffe
    /// warten, bevor sie überhaupt erscheint.
    /// </remarks>
    internal sealed class DashboardCoverProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService? _coverService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly MicrosoftDispatcherQueue? _dispatcherQueue;

        // Zuordnung Folgen-Id → offene Kacheln, damit der Hintergrund-Rückruf den passenden
        // Kachelsatz mit dem nachgeladenen Cover versorgen kann.
        private readonly Dictionary<Guid, List<NewEpisodeCardViewModel>> _pendingEpisodeCoverCards = [];

        // Cover-Bytes aus der Datenbank für den laufenden Durchgang. Einmal gefüllt, in allen
        // Abschnitten wiederverwendet — statt einer Abfrage je Kachel.
        private IReadOnlyDictionary<Guid, byte[]> _episodeCoverBytesCache = new Dictionary<Guid, byte[]>();

        private ICoverViewModelFactory? _coverFactory;

        /// <summary>
        /// Initialisiert die Cover-Beschaffung.
        /// </summary>
        /// <param name="scopeFactory">Für datenbanknahe Cover-Abfragen.</param>
        /// <param name="coverService">Zentraler Cover-Dienst. Ohne ihn bleibt der Zwischenspeicher leer.</param>
        /// <param name="backgroundCoverService">Trägt fehlende Folgen-Cover nach. Ohne ihn entfällt das Nachtragen.</param>
        /// <param name="dispatcherQueue">Oberflächen-Warteschlange für die Bilderzeugung. Ohne sie läuft die Umwandlung auf einem Hintergrund-Task.</param>
        public DashboardCoverProvider(
            IServiceScopeFactory scopeFactory,
            ICoverService? coverService,
            BackgroundCoverService? backgroundCoverService,
            MicrosoftDispatcherQueue? dispatcherQueue)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _backgroundCoverService = backgroundCoverService;
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>
        /// Beginnt einen Durchgang: verwirft die Vormerkungen des vorigen und liest die
        /// bekannten Folgen-Cover in einer Abfrage.
        /// </summary>
        /// <param name="relevantEpisodeIds">Die Folgen, deren Cover sichtbar werden können.</param>
        /// <param name="cancellationToken">Bricht die Abfrage ab.</param>
        public async Task BeginSessionAsync(IReadOnlyList<Guid> relevantEpisodeIds, CancellationToken cancellationToken = default)
        {
            _pendingEpisodeCoverCards.Clear();
            _episodeCoverBytesCache = _coverService is not null && relevantEpisodeIds.Count > 0
                ? await _coverService.GetEpisodeCoverBytesAsync(relevantEpisodeIds, cancellationToken)
                : new Dictionary<Guid, byte[]>();
        }

        /// <summary>
        /// Reicht die vorgemerkten Kacheln an den Hintergrunddienst weiter, damit ihre
        /// Folgen-Cover nachgeladen werden. Ohne Hintergrunddienst geschieht nichts.
        /// </summary>
        public void FlushPendingRefresh()
        {
            if (_backgroundCoverService is null || _pendingEpisodeCoverCards.Count == 0)
            {
                return;
            }

            Dictionary<Guid, List<NewEpisodeCardViewModel>> snapshot = new(_pendingEpisodeCoverCards);
            List<Guid> ids = [.. snapshot.Keys];
            _pendingEpisodeCoverCards.Clear();

            _backgroundCoverService.EnqueueForEpisodes(ids, (episodeId, bytes) =>
            {
                if (snapshot.TryGetValue(episodeId, out List<NewEpisodeCardViewModel>? cards))
                {
                    DispatchCoverUpdate(cards, bytes);
                }
            });
        }

        /// <summary>
        /// Ermittelt das Cover, mit dem eine Kachel zuerst erscheint.
        /// </summary>
        /// <param name="series">Die Serie der Kachel; liefert das Ersatzbild.</param>
        /// <param name="episodeId">Die Folge der Kachel, oder <see cref="Guid.Empty"/>.</param>
        /// <returns>
        /// Das Bild und die Angabe, ob es das echte Folgen-Cover ist. Beim Ersatzbild ist die
        /// Angabe <see langword="false"/> — die Kachel gehört dann ins Nachtragen.
        /// </returns>
        public async Task<(BitmapImage? Cover, bool HasEpisodeCover)> ResolveCardCoverAsync(Series series, Guid episodeId)
        {
            if (episodeId != Guid.Empty)
            {
                BitmapImage? episodeCover = await TryLoadEpisodeCoverAsync(episodeId);
                if (episodeCover is not null)
                {
                    return (episodeCover, true);
                }
            }

            return (await BuildSeriesCoverAsync(series), false);
        }

        /// <summary>
        /// Liefert das Folgen-Cover aus dem Zwischenspeicher dieses Durchgangs, sofern es
        /// dort liegt. Kein Dateizugriff, keine Abfrage.
        /// </summary>
        /// <param name="episodeId">Die gesuchte Folge.</param>
        /// <returns>Das Bild oder <see langword="null"/>.</returns>
        public async Task<BitmapImage?> TryGetCachedEpisodeCoverAsync(Guid episodeId)
        {
            if (episodeId == Guid.Empty || !_episodeCoverBytesCache.TryGetValue(episodeId, out byte[]? bytes))
            {
                return null;
            }

            // Die Umwandlung hängt am Oberflächen-Thread und ist sehr kurz; ein von außen
            // gereichtes Abbruchzeichen brächte hier nichts.
            return await CoverService.ConvertToBitmapAsync(bytes, cancellationToken: CancellationToken.None);
        }

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Serie.
        /// Priorität: DB-Cover → cover.jpg im Serienordner → URL-Cover → null.
        /// </summary>
        /// <param name="series">Die Serie.</param>
        /// <returns>Das Bild oder <see langword="null"/>.</returns>
        public Task<BitmapImage?> BuildSeriesCoverAsync(Series series) =>
            CoverFactory.BuildSeriesCoverAsync(series);

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Folge.
        /// Priorität: DB-Cover → cover.jpg im Ordner → Kennzeichnung des ersten Stücks → null.
        /// </summary>
        /// <param name="episode">Die Folge.</param>
        /// <returns>Das Bild oder <see langword="null"/>.</returns>
        public Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode) =>
            CoverFactory.BuildEpisodeCoverAsync(episode);

        /// <summary>
        /// Merkt eine Kachel vor, die ihr Folgen-Cover nachgereicht bekommen soll.
        /// </summary>
        /// <param name="episodeId">Die Folge, deren Cover fehlt.</param>
        /// <param name="card">Die betroffene Kachel.</param>
        public void TrackPending(Guid episodeId, NewEpisodeCardViewModel card)
        {
            if (episodeId == Guid.Empty)
            {
                return;
            }

            if (!_pendingEpisodeCoverCards.TryGetValue(episodeId, out List<NewEpisodeCardViewModel>? list))
            {
                list = [];
                _pendingEpisodeCoverCards[episodeId] = list;
            }

            list.Add(card);
        }

        // Wird beim ersten Cover-Aufruf erzeugt und danach weiterverwendet.
        private ICoverViewModelFactory CoverFactory =>
            _coverFactory ??= new CoverViewModelFactory(_scopeFactory, _coverService);

        /// <summary>
        /// Holt das Folgen-Cover: erst aus dem Zwischenspeicher des Durchgangs, sonst mit
        /// einer einzelnen Abfrage. Kein Dateizugriff.
        /// </summary>
        private async Task<BitmapImage?> TryLoadEpisodeCoverAsync(Guid episodeId)
        {
            BitmapImage? fromCache = await TryGetCachedEpisodeCoverAsync(episodeId);
            if (fromCache is not null || _episodeCoverBytesCache.ContainsKey(episodeId) || _coverService is null)
            {
                return fromCache;
            }

            // Die Kachel war nicht im Vorablauf dabei — einzelne Abfrage, kein Dateisystem.
            return await _coverService.GetEpisodeCoverImageAsync(episodeId);
        }

        /// <summary>
        /// Erzeugt das Bild auf dem Oberflächen-Thread und frischt jede vorgemerkte Kachel auf.
        /// Ohne Warteschlange (Tests) läuft die Umwandlung auf einem Hintergrund-Task; das
        /// genügt dort, weil ein Bild ohne laufende Oberfläche ohnehin nichts anzeigt.
        /// </summary>
        private void DispatchCoverUpdate(IReadOnlyList<NewEpisodeCardViewModel> cards, byte[] bytes)
        {
            if (_dispatcherQueue is not null)
            {
                _ = _dispatcherQueue.TryEnqueue(async () => await ApplyCoverAsync(cards, bytes));
                return;
            }

            _ = Task.Run(async () => await ApplyCoverAsync(cards, bytes));
        }

        /// <summary>Wandelt die Bytes um und setzt das Bild auf jeder Kachel.</summary>
        private static async Task ApplyCoverAsync(IReadOnlyList<NewEpisodeCardViewModel> cards, byte[] bytes)
        {
            BitmapImage? bitmap = await CoverService.ConvertToBitmapAsync(bytes);
            if (bitmap is null)
            {
                return;
            }

            foreach (NewEpisodeCardViewModel card in cards)
            {
                card.UpdateCoverImage(bitmap);
            }
        }
    }
}
