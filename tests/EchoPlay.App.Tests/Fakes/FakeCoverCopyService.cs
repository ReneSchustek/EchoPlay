using EchoPlay.Data.Services.Interfaces;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ICoverCopyService"/>.
    /// Tut nichts und meldet null kopierte Cover – ausreichend für Tests, die nur
    /// den Aufrufpfad der Episoden-Pipeline prüfen wollen.
    /// </summary>
    internal sealed class FakeCoverCopyService : ICoverCopyService
    {
        /// <summary>Anzahl der Aufrufe, für Assertions in Tests.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc/>
        public Task<int> CopyFromMatchingEpisodesAsync(Guid targetSeriesId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(0);
        }
    }
}
