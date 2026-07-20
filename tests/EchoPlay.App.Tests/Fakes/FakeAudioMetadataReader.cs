using EchoPlay.LocalLibrary.Metadata;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IAudioMetadataReader"/>.
    /// Gibt vorab konfigurierte Metadaten zurück, ohne Dateisystemzugriffe durchzuführen.
    /// </summary>
    internal sealed class FakeAudioMetadataReader : IAudioMetadataReader
    {
        private readonly Dictionary<string, (TimeSpan Duration, int TrackNumber)> _metadataByPath;
        private readonly (TimeSpan Duration, int TrackNumber) _defaultMetadata;

        /// <summary>
        /// Erstellt den Fake mit optionalen pfadspezifischen Metadaten.
        /// </summary>
        /// <param name="metadataByPath">Metadaten pro Dateipfad. Für nicht vorhandene Pfade gilt der Standardwert.</param>
        /// <param name="defaultDuration">Standarddauer für nicht konfigurierte Pfade.</param>
        /// <param name="defaultTrackNumber">Standard-Tracknummer für nicht konfigurierte Pfade.</param>
        public FakeAudioMetadataReader(
            Dictionary<string, (TimeSpan, int)>? metadataByPath = null,
            TimeSpan defaultDuration = default,
            int defaultTrackNumber = 0)
        {
            _metadataByPath = metadataByPath ?? [];
            _defaultMetadata = (defaultDuration, defaultTrackNumber);
        }

        /// <inheritdoc/>
        public (TimeSpan Duration, int TrackNumber) Read(string filePath)
        {
            if (_metadataByPath.TryGetValue(filePath, out (TimeSpan, int) metadata))
            {
                return metadata;
            }

            return _defaultMetadata;
        }
    }
}
