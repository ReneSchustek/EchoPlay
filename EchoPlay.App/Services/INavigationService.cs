namespace EchoPlay.App.Services
{
    /// <summary>
    /// Abstrahiert den Seitenwechsel innerhalb der Haupt-ContentFrame-Shell.
    /// ViewModels und Views rufen ausschließlich diesen Dienst auf, nie direkt
    /// <c>Frame.Navigate</c>. So bleiben ViewModels testbar ohne WinUI-Abhängigkeit
    /// und jede Seite weiß nur, welches logische Ziel sie ansteuern will –
    /// nicht welche Page-Klasse tatsächlich dahintersteht.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>Gibt an, ob ein Rückwärts-Sprung möglich ist.</summary>
        bool CanGoBack { get; }

        /// <summary>
        /// Navigiert zur angegebenen Ziel-Seite.
        /// </summary>
        /// <param name="target">Logische Seite.</param>
        /// <param name="parameter">Optionaler Navigationsparameter (z. B. SeriesId).</param>
        void NavigateTo(NavigationTarget target, object? parameter = null);

        /// <summary>
        /// Geht eine Ebene zurück, falls möglich.
        /// </summary>
        /// <returns><c>true</c> wenn tatsächlich zurück gesprungen wurde.</returns>
        bool GoBack();
    }
}
