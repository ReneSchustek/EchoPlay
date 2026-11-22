namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Zahlen der Info-Leiste: abonnierte Serien und gehörte, offene und neue Folgen.
    /// </summary>
    /// <param name="SubscribedSeries">Abonnierte Serien.</param>
    /// <param name="FinishedEpisodes">Folgen mit abgeschlossenem Hörstand.</param>
    /// <param name="UnfinishedEpisodes">Alle übrigen Folgen.</param>
    /// <param name="NewEpisodes">Offene Folgen, die bereits erschienen sind.</param>
    internal sealed record LibraryCounts(int SubscribedSeries, int FinishedEpisodes, int UnfinishedEpisodes, int NewEpisodes);
}
