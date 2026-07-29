using EchoPlay.App.Services;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IInstallerLauncher"/>. Protokolliert Startanfragen, statt sie
    /// auszuführen — ein echter Start würde eine ausführbare Datei aus dem Temp-Verzeichnis
    /// starten, nur weil ein Test den Download-Pfad bis zum Ende durchläuft.
    /// </summary>
    internal sealed class FakeInstallerLauncher : IInstallerLauncher
    {
        private readonly bool _startSucceeds;

        /// <summary>
        /// Erstellt den Fake.
        /// </summary>
        /// <param name="startSucceeds">Ergebnis, das <see cref="Start"/> melden soll.</param>
        public FakeInstallerLauncher(bool startSucceeds = true) => _startSucceeds = startSucceeds;

        /// <summary>Alle angefragten Setup-Pfade – für Assertions.</summary>
        public List<string> StartedPaths { get; } = [];

        /// <inheritdoc/>
        public bool Start(string setupPath)
        {
            StartedPaths.Add(setupPath);
            return _startSucceeds;
        }
    }
}
