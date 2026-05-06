using EchoPlay.App.Services;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IOnlineAccessGuard"/>.
    /// Simuliert den Bestätigungsdialog im Offline-Modus ohne WinUI-Abhängigkeit.
    /// Gibt konfigurierbar entweder ein Disposable (Nutzer bestätigt) oder null (Nutzer lehnt ab) zurück.
    /// </summary>
    internal sealed class FakeOnlineAccessGuard : IOnlineAccessGuard
    {
        private readonly bool _allowAccess;

        /// <summary>Anzahl der Aufrufe von <see cref="RequestOnlineAccessAsync"/>.</summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Initialisiert den Fake.
        /// </summary>
        /// <param name="allowAccess">
        /// <see langword="true"/> simuliert "Ja" im Dialog (oder Online-Modus),
        /// <see langword="false"/> simuliert "Nein" (Aktion abbrechen).
        /// Standard: <see langword="true"/>.
        /// </param>
        public FakeOnlineAccessGuard(bool allowAccess = true)
        {
            _allowAccess = allowAccess;
        }

        /// <inheritdoc/>
        public Task<IDisposable?> RequestOnlineAccessAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (_allowAccess)
            {
                return Task.FromResult<IDisposable?>(new NoOpDisposable());
            }

            return Task.FromResult<IDisposable?>(null);
        }

        /// <summary>
        /// Zustandsloses Disposable – Dispose tut nichts.
        /// </summary>
        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
