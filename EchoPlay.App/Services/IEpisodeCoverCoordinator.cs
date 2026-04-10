using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Koordiniert das Setzen, Suchen und Anwenden von Cover-Bildern für lokale
    /// Serien und Episoden. Kapselt die Bestätigungs-Dialoge beim Überschreiben,
    /// den HTTP-Download eines gewählten Cover-Hits, das Speichern in der
    /// CoverImages-Tabelle, das optionale Schreiben von <c>cover.jpg</c> und das
    /// abschließende Update der Card-Bitmap. Aus dem MediathekLokalViewModel ausgelagert.
    /// </summary>
    public interface IEpisodeCoverCoordinator
    {
        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Begriff und liefert die Treffer
        /// als App-eigene <see cref="CoverSearchHit"/>-Wrapper.
        /// </summary>
        /// <param name="query">Suchbegriff – meist der Serien- oder Folgentitel.</param>
        /// <param name="ct">Abbruchtoken, z.B. für Dialog-Schließen.</param>
        Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(string query, CancellationToken ct);

        /// <summary>
        /// Übernimmt rohe Bytes als Serien-Cover. Fragt vor dem Überschreiben nach,
        /// wenn die Karte bereits ein Cover hat.
        /// </summary>
        Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes);

        /// <summary>
        /// Übernimmt rohe Bytes als Episoden-Cover. Fragt vor dem Überschreiben nach.
        /// </summary>
        Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes);

        /// <summary>
        /// Lädt das Cover-Bild zum gewählten <see cref="CoverSearchHit"/> herunter
        /// und übernimmt es als Serien-Cover. Bei Download-Fehler erscheint ein Hinweisdialog.
        /// </summary>
        Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit);

        /// <summary>
        /// Lädt das Cover-Bild zum gewählten <see cref="CoverSearchHit"/> herunter
        /// und übernimmt es als Episoden-Cover. Bei Download-Fehler erscheint ein Hinweisdialog.
        /// </summary>
        Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit);
    }
}
