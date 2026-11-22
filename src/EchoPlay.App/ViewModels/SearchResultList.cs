using System.Collections.ObjectModel;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Trefferliste der Suche: Anbieter-Karten stehen nach ihrem Rang zum Suchbegriff,
    /// Karten aus dem eigenen Bestand dahinter.
    /// </summary>
    internal sealed class SearchResultList
    {
        // Der Rang jeder Anbieter-Karte, in derselben Reihenfolge wie die Karten selbst.
        // Er sagt beim Einfügen, wo die nächste Karte hingehört. Karten aus dem eigenen
        // Bestand stehen dahinter und brauchen keinen Rang.
        private readonly List<SearchResultRank> _onlineRanks = [];

        /// <summary>
        /// Die Karten. Eine feste Instanz, die nur geleert und befüllt wird: Die Treffer erscheinen
        /// einzeln, während die Suche noch läuft. Ein Austausch der ganzen Liste würde die
        /// Bindung bei jedem Treffer neu aufziehen.
        /// </summary>
        public ObservableCollection<SearchResultViewModel> Items { get; } = [];

        /// <summary>
        /// Reiht eine Anbieter-Karte an ihrem Rang ein.
        /// </summary>
        /// <remarks>
        /// Gesucht wird die erste Stelle, an der die neue Karte vor der bereits stehenden
        /// rangiert. Eine spät eintreffende, bessere Karte rückt damit nach oben. Der Preis
        /// dafür, dass überhaupt schon etwas zu sehen ist, statt auf die vollständige Liste
        /// zu warten.
        /// </remarks>
        /// <param name="card">Die fertige Trefferkarte.</param>
        /// <param name="rank">Ihr Rang zum Suchbegriff.</param>
        public void InsertOnline(SearchResultViewModel card, SearchResultRank rank)
        {
            ArgumentNullException.ThrowIfNull(card);

            int index = 0;
            while (index < _onlineRanks.Count && !rank.RanksBefore(_onlineRanks[index]))
            {
                index++;
            }

            _onlineRanks.Insert(index, rank);
            Items.Insert(index, card);
        }

        /// <summary>Hängt eine Karte aus dem eigenen Bestand hinten an.</summary>
        /// <param name="card">Die fertige Trefferkarte.</param>
        public void AddLocal(SearchResultViewModel card)
        {
            ArgumentNullException.ThrowIfNull(card);
            Items.Add(card);
        }

        /// <summary>
        /// Gibt die Cover aller Karten frei. Pro Treffer hängt ein Bild; ohne Freigabe steigen
        /// die Heap-Bytes nach jeder Suche an, weil die Bindungen die alten Karten noch kurz halten.
        /// </summary>
        public void ReleaseCovers()
        {
            foreach (SearchResultViewModel result in Items)
            {
                result.ClearCoverImage();
            }
        }

        /// <summary>Gibt die Cover frei und leert Karten samt Rangfolge.</summary>
        public void Clear()
        {
            ReleaseCovers();
            _onlineRanks.Clear();
            Items.Clear();
        }
    }
}
