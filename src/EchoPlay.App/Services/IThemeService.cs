namespace EchoPlay.App.Services
{
    /// <summary>
    /// Theme-Wechsel zur Laufzeit. ViewModels und Tests hängen an dieser Schnittstelle statt an
    /// <see cref="ThemeService"/>, der die Ressourcen der laufenden Anwendung umbaut.
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Wechselt das aktive Theme zur Laufzeit.
        /// Unbekannte Namen werden ignoriert.
        /// </summary>
        /// <param name="themeName">Name des gewünschten Themes. Gültig: "Ruhrcoder", "ModernClassic", "MidnightLibrary", "PaperCoffee", "ForestSignal", "AmberWhiskey".</param>
        void ApplyTheme(string themeName);
    }
}
