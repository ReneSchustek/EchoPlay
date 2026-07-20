namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für Theme-Wechsel zur Laufzeit.
    /// Ermöglicht die Entkopplung von ViewModels und Tests von der konkreten <see cref="ThemeService"/>-Implementierung.
    /// </summary>

    public interface IThemeService
    {
        /// <summary>
        /// Wechselt das aktive Theme zur Laufzeit.
        /// Unbekannte Namen werden ignoriert.
        /// </summary>
        /// <param name="themeName">Name des gewünschten Themes. Gültig: "MidnightLibrary", "ModernClassic", "PaperCoffee".</param>
        void ApplyTheme(string themeName);
    }
}
