using EchoPlay.Data.Services.Interfaces;
using System;
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

        /// <summary>Anzahl der PurgeAsync-Aufrufe.</summary>
        public int PurgeCount { get; private set; }

        /// <summary>Aufbewahrungstage des letzten PurgeAsync-Aufrufs.</summary>
        public int LastPurgeRetentionDays { get; private set; } = -1;

        /// <summary>Anzahl der VacuumAsync-Aufrufe.</summary>
        public int VacuumCount { get; private set; }

        /// <summary>Ist ein Text gesetzt, scheitert jede Pflege- und Reset-Operation damit.</summary>
        public string? FailureMessage { get; set; }

        public Task PurgeAsync(int retentionDays)
        {
            PurgeCount++;
            LastPurgeRetentionDays = retentionDays;
            return FailOrComplete();
        }

        public Task VacuumAsync()
        {
            VacuumCount++;
            return FailOrComplete();
        }

        /// <summary>Anzahl der OptimizeAsync-Aufrufe.</summary>
        public int OptimizeCount { get; private set; }

        public Task OptimizeAsync()
        {
            OptimizeCount++;
            return FailOrComplete();
        }

        public Task ClearLibraryAsync()
        {
            ClearAllCount++;
            return FailOrComplete();
        }

        public Task ClearOnlineLibraryAsync()
        {
            ClearOnlineCount++;
            return FailOrComplete();
        }

        public Task ClearLocalLibraryAsync()
        {
            ClearLocalCount++;
            return FailOrComplete();
        }

        private Task FailOrComplete() =>
            FailureMessage is null
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException(FailureMessage));
    }
}
