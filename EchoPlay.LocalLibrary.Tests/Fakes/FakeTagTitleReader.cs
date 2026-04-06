using EchoPlay.LocalLibrary.Metadata;
using System.Collections.Generic;

namespace EchoPlay.LocalLibrary.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITagTitleReader"/>.
    /// Gibt vorab konfigurierte Tag-Daten zurück, ohne Dateisystemzugriffe durchzuführen.
    /// Nicht konfigurierte Pfade liefern die Standardwerte (standardmäßig leere Strings).
    /// </summary>
    internal sealed class FakeTagTitleReader : ITagTitleReader
    {
        private readonly Dictionary<string, (string Title, string Album)> _tagsByPath;
        private readonly string _defaultTitle;
        private readonly string _defaultAlbum;

        /// <summary>
        /// Erstellt den Fake mit optionalen pfadspezifischen Tag-Daten.
        /// </summary>
        /// <param name="tagsByPath">Tag-Daten pro Dateipfad. Für nicht vorhandene Pfade gilt der Standardwert.</param>
        /// <param name="defaultTitle">Standard-Titel für nicht konfigurierte Pfade.</param>
        /// <param name="defaultAlbum">Standard-Album für nicht konfigurierte Pfade.</param>
        public FakeTagTitleReader(
            Dictionary<string, (string, string)>? tagsByPath = null,
            string defaultTitle = "",
            string defaultAlbum = "")
        {
            _tagsByPath   = tagsByPath ?? [];
            _defaultTitle = defaultTitle;
            _defaultAlbum = defaultAlbum;
        }

        /// <inheritdoc/>
        public (string Title, string Album) Read(string filePath)
        {
            if (_tagsByPath.TryGetValue(filePath, out (string, string) tags))
            {
                return tags;
            }

            return (_defaultTitle, _defaultAlbum);
        }
    }
}
