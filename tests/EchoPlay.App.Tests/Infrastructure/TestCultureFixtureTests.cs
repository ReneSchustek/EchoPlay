using System.Globalization;
using Xunit;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Wächter-Test: Stellt sicher, dass der xunit.runner.json-Eintrag <c>"culture": "de-DE"</c>
    /// noch greift. Fällt der Eintrag heraus, kippen kulturabhängige Assertions (z. B. Monatsnamen
    /// im Dashboard-Gruppierungs-Test) auf en-US-CI-Runnern lautlos durch — dieser Test fängt das
    /// Versehen sofort beim ersten Lauf ab.
    ///
    /// Bewusst nur <see cref="CultureInfo.CurrentCulture"/>: xUnit setzt
    /// <see cref="CultureInfo.CurrentUICulture"/> nicht mit; eine UICulture-Assertion wäre Über-Assertion
    /// und würde den Wächter spröde machen, ohne tatsächlich abgesichertes Verhalten zu prüfen.
    /// </summary>
    public sealed class TestCultureFixtureTests
    {
        [Fact]
        public void CurrentCulture_IsPinnedToGerman()
        {
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        }
    }
}
