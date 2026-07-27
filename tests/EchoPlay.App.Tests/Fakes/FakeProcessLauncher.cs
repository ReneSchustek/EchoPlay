using EchoPlay.App.Services;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IProcessLauncher"/>. Protokolliert Startanfragen, statt sie
    /// auszuführen — ein echter Start würde im Testhost den Testrunner selbst neu starten.
    /// </summary>
    internal sealed class FakeProcessLauncher : IProcessLauncher
    {
        private readonly bool _startSucceeds;

        /// <summary>
        /// Erstellt den Fake.
        /// </summary>
        /// <param name="currentExecutablePath">Rückgabewert für <see cref="CurrentExecutablePath"/>.</param>
        /// <param name="startSucceeds">Ergebnis, das <see cref="Start"/> melden soll.</param>
        public FakeProcessLauncher(string? currentExecutablePath = @"C:\Programs\EchoPlay\EchoPlay.App.exe", bool startSucceeds = true)
        {
            CurrentExecutablePath = currentExecutablePath;
            _startSucceeds = startSucceeds;
        }

        /// <inheritdoc/>
        public string? CurrentExecutablePath { get; }

        /// <summary>Alle angefragten Startpfade – für Assertions.</summary>
        public List<string> StartedPaths { get; } = [];

        /// <inheritdoc/>
        public bool Start(string executablePath)
        {
            StartedPaths.Add(executablePath);
            return _startSucceeds;
        }
    }
}
