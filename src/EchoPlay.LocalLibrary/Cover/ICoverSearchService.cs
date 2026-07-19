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
    }
}
