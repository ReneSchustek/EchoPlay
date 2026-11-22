namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die drei Teil-ViewModels der Online-Mediathek. Die Aktionen bekommen sie gebündelt, weil
    /// sie immer gemeinsam auftreten und unverändert vom ViewModel der Seite kommen.
    /// </summary>
    /// <param name="Series">Die Serienliste.</param>
    /// <param name="Episodes">Die Folgen der gewählten Serie.</param>
    /// <param name="ProviderSearch">Die Treffer der Anbietersuche.</param>
    internal sealed record OnlineLibraryParts(
        OnlineSeriesViewModel Series,
        OnlineEpisodesViewModel Episodes,
        OnlineProviderSearchViewModel ProviderSearch);
}
