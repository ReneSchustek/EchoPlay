using EchoPlay.App.Services;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalizationService"/>.
    /// Gibt standardmäßig den Schlüssel selbst zurück – in Tests reicht das,
    /// um zu prüfen, ob der richtige Schlüssel angefragt wird.
    /// </summary>
    /// <remarks>
    /// Für Texte mit Platzhaltern (<c>{0}</c>) genügt das nicht: Der Schlüssel als Muster
    /// verschluckt den eingesetzten Wert. Solche Tests geben die Texte deshalb mit.
    /// </remarks>
    internal sealed class FakeLocalizationService : ILocalizationService
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        /// <summary>
        /// Erstellt den Fake.
        /// </summary>
        /// <param name="values">Feste Texte je Schlüssel; fehlt einer, kommt der Schlüssel zurück.</param>
        public FakeLocalizationService(IReadOnlyDictionary<string, string>? values = null)
        {
            _values = values ?? new Dictionary<string, string>();
        }

        /// <inheritdoc/>
        public string Get(string key) =>
            _values.TryGetValue(key, out string? value) ? value : key;
    }
}
