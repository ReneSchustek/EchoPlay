using EchoPlay.App.Helpers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Statuszeilen des Startbilds. Sie stehen im Startbild und kommen deshalb in der
    /// Sprache der Oberfläche.
    /// </summary>
    internal static class SplashStatus
    {
        /// <summary>Liest eine Statuszeile aus den Ressourcen.</summary>
        /// <param name="key">Schlüssel der Zeile.</param>
        /// <param name="fallback">Text, wenn die Ressourcen fehlen (Testprozess).</param>
        /// <returns>Die Zeile in der Sprache der Oberfläche.</returns>
        public static string Text(string key, string fallback) => SafeResourceLoader.Get(key, fallback);
    }
}
