using System.Globalization;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Programmatischer Kultur-Pin als Defense-in-Depth fuer den
    /// xunit.runner.json-Eintrag <c>"culture": "de-DE"</c>. Auf GitHub-Windows-Runnern
    /// (en-US-Default) hat sich gezeigt, dass die JSON-Variante nicht zuverlaessig
    /// greift, weil der VSTest-Adapter den Pfad zur JSON-Datei je nach Build-Konfig
    /// nicht findet. Der ModuleInitializer wird vom CLR vor jedem Test-Lauf in
    /// dieser Assembly ausgefuehrt — unabhaengig davon, ob xunit.runner.json
    /// kopiert wurde oder nicht.
    ///
    /// Nur fuer App.Tests bewusst gepinnt: andere Test-Projekte koennten kulturneutral
    /// laufen und sollen sich nicht den Default abdrehen lassen.
    /// </summary>
    internal static class TestCultureModuleInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.DefaultThreadCurrentCulture = german;
            CultureInfo.DefaultThreadCurrentUICulture = german;
        }
    }
}
