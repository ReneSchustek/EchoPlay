using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht online nach Cover-Kandidaten für einen Serien- oder Folgentitel.
    /// Der Nutzer wählt aus den zurückgegebenen Kandidaten das passende Cover aus.
    /// </summary>
    public interface ICoverSearchService
    {
        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Suchbegriff.
        /// Die Ergebnisse kommen aus der Cover Art Archive-Datenbank (Teil von MusicBrainz).
        /// </summary>
        /// <param name="title">Suchbegriff – typischerweise Serien- oder Folgentitel.</param>
        /// <param name="ct">Abbruchtoken für den Fall, dass der Nutzer abbricht.</param>
        /// <returns>
        /// Geordnete Liste von Kandidaten. Gibt eine leere Liste zurück wenn keine Ergebnisse
        /// gefunden wurden – niemals <see langword="null"/>.
        /// </returns>
        Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default);

        /// <summary>
        /// Sucht einen weiteren Abschnitt der Trefferliste — für „Weitere Ergebnisse laden".
        /// </summary>
        /// <remarks>
        /// Die Anbieter begrenzen ihre Antwort (neun Treffer, bei Deezer-Künstlern sechs). Wer
        /// darunter nichts Passendes findet, konnte bisher nur den Suchbegriff ändern. Nachladen
        /// heißt deshalb: **erneut abfragen** mit Versatz, nicht nachfiltern, was schon da ist.
        /// </remarks>
        /// <param name="title">Suchbegriff – typischerweise Serien- oder Folgentitel.</param>
        /// <param name="page">Welcher Abschnitt geholt wird; <see cref="CoverSearchPage.First"/> entspricht der Überladung ohne Seite.</param>
        /// <param name="ct">Abbruchtoken für den Fall, dass der Nutzer abbricht.</param>
        /// <returns>
        /// Die Kandidaten dieses Abschnitts. Leere Liste heißt „nichts mehr da" — der Aufrufer
        /// blendet das Nachladen danach aus.
        /// </returns>
        Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default);
    }
}
