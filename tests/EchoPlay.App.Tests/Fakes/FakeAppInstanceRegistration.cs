using EchoPlay.App.Services;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IAppInstanceRegistration"/>. Merkt sich die Aufrufe, statt die
    /// Windows-Laufzeit anzusprechen, die im Testhost nicht registriert ist.
    /// </summary>
    internal sealed class FakeAppInstanceRegistration : IAppInstanceRegistration
    {
        /// <summary>Die Aufrufe in ihrer Reihenfolge: <c>Release</c> oder <c>Reclaim</c>.</summary>
        public List<string> Calls { get; } = [];

        /// <summary>Läuft beim Freigeben mit, damit ein Test den Zustand in diesem Moment festhalten kann.</summary>
        public Action? OnRelease { get; set; }

        /// <inheritdoc/>
        public void Release()
        {
            Calls.Add(nameof(Release));
            OnRelease?.Invoke();
        }

        /// <inheritdoc/>
        public void Reclaim() => Calls.Add(nameof(Reclaim));
    }
}
