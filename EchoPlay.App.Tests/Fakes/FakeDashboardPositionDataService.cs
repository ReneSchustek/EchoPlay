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
