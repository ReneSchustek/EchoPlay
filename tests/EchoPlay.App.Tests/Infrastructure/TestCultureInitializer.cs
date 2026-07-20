using System.Globalization;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Defense-in-Depth zum xunit.runner.json-Culture-Pin (de-DE): setzt vor jedem
    /// Test-Discover die Default-Culture programmatisch. Greift selbst dann, wenn
    /// xunit.runner.json aus dem Output-Verzeichnis verloren geht oder der v3-Runner
    /// die Konfig-Datei anders auflöst. Ohne diesen Init scheitern kulturabhängige
    /// Tests (z. B. Monatsnamen-Gruppierung) auf en-US-CI-Runnern.
    /// </summary>
    internal static class TestCultureInitializer
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            CultureInfo german = new("de-DE");
            CultureInfo.DefaultThreadCurrentCulture = german;
            CultureInfo.DefaultThreadCurrentUICulture = german;
        }
    }
}
