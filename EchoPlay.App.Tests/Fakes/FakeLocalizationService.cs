using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalizationService"/>.
    /// Gibt den Schlüssel selbst als Wert zurück – in Tests reicht das,
    /// um zu prüfen ob der richtige Key angefragt wird.
    /// </summary>
    internal sealed class FakeLocalizationService : ILocalizationService
    {
        /// <inheritdoc/>
        public string Get(string key) => key;
    }
}
