using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IDashboardPositionDataService"/>.
    /// Speichert Positionen in-memory – kein Datenbankzugriff.
    /// </summary>
    internal sealed class FakeDashboardPositionDataService : IDashboardPositionDataService
    {
        private readonly Dictionary<string, List<DashboardPosition>> _positions = [];
        private readonly Exception? _saveFailure;

        /// <summary>
        /// Erstellt den Nachbau.
        /// </summary>
        /// <param name="saveFailure">
        /// Wird beim Speichern geworfen, wenn gesetzt. Die echte Ablage kann während einer
        /// Wartung gesperrt sein — ohne diesen Fall bliebe der Behandlungszweig ungeprüft.
        /// </param>
        public FakeDashboardPositionDataService(Exception? saveFailure = null)
        {
            _saveFailure = saveFailure;
        }

        /// <summary>Wie oft eine Reihenfolge gespeichert wurde.</summary>
        public int SaveCallCount { get; private set; }

        /// <summary>Die zuletzt gespeicherte Reihenfolge.</summary>
        public IReadOnlyList<Guid> LastOrder { get; private set; } = [];

        /// <inheritdoc />
        public Task<IReadOnlyList<DashboardPosition>> GetBySectionAsync(string section, CancellationToken cancellationToken = default)
        {
            if (_positions.TryGetValue(section, out List<DashboardPosition>? list))
            {
                return Task.FromResult<IReadOnlyList<DashboardPosition>>(list);
            }

            return Task.FromResult<IReadOnlyList<DashboardPosition>>([]);
        }

        /// <inheritdoc />
        public Task SaveOrderAsync(string section, IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            LastOrder = [.. seriesIds];

            if (_saveFailure is not null)
            {
                return Task.FromException(_saveFailure);
            }

            List<DashboardPosition> list = [];

            for (int i = 0; i < seriesIds.Count; i++)
            {
                list.Add(new DashboardPosition
                {
                    SeriesId = seriesIds[i],
                    Section = section,
                    Position = i
                });
            }

            _positions[section] = list;
            return Task.CompletedTask;
        }
    }
}
