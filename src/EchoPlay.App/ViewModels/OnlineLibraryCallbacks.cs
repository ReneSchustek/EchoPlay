namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Rückrufe, mit denen die Aktionen den Zustand der Online-Mediathek setzen.
    /// </summary>
    /// <param name="SetIsLoading">Schaltet die Ladeanzeige.</param>
    /// <param name="SetLoadingStatusText">Setzt den Statustext während des Ladens.</param>
    /// <param name="SetHasNoProvider">Meldet, dass kein Anbieter eingerichtet ist.</param>
    /// <param name="ReloadAfterImportAsync">Lädt die Mediathek nach einem Import neu.</param>
    internal sealed record OnlineLibraryCallbacks(
        Action<bool> SetIsLoading,
        Action<string> SetLoadingStatusText,
        Action<bool> SetHasNoProvider,
        Func<Task> ReloadAfterImportAsync);
}
