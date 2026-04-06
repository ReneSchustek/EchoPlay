using EchoPlay.Data.Services.Interfaces;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IDatabaseMaintenanceService"/>.
    /// Zählt die Aufrufe, ohne echte DB-Operationen auszuführen.
    /// </summary>
    internal sealed class FakeDatabaseMaintenanceService : IDatabaseMaintenanceService
    {
        /// <summary>Anzahl der ClearLibraryAsync-Aufrufe.</summary>
        public int ClearAllCount { get; private set; }

        /// <summary>Anzahl der ClearOnlineLibraryAsync-Aufrufe.</summary>
        public int ClearOnlineCount { get; private set; }

        /// <summary>Anzahl der ClearLocalLibraryAsync-Aufrufe.</summary>
        public int ClearLocalCount { get; private set; }

        public Task PurgeAsync(int retentionDays) => Task.CompletedTask;
        public Task VacuumAsync() => Task.CompletedTask;
        public Task OptimizeAsync() => Task.CompletedTask;

        public Task ClearLibraryAsync()
        {
            ClearAllCount++;
            return Task.CompletedTask;
        }

        public Task ClearOnlineLibraryAsync()
        {
            ClearOnlineCount++;
            return Task.CompletedTask;
        }

        public Task ClearLocalLibraryAsync()
        {
            ClearLocalCount++;
            return Task.CompletedTask;
        }
    }
}
