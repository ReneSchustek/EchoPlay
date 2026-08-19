using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IEpisodeImportSource"/>.
    /// Gibt eine vorab konfigurierte Episodenliste zurück, ohne externe APIs aufzurufen.
    /// </summary>
    internal sealed class FakeEpisodeImportSource : IEpisodeImportSource
    {
        private readonly IReadOnlyList<ImportEpisode> _episodes;

        /// <summary>
        /// Erstellt den Fake mit festen Rückgabewerten.
        /// </summary>
        /// <param name="episodes">Die zurückzugebende Episodenliste.</param>
        /// <param name="failForSourceSeriesId">
        /// Für diese Serienkennung wird geworfen statt geliefert. Beim Prüfen aller Serien
        /// scheitert im Betrieb regelmäßig eine einzelne — nur so lässt sich zeigen, dass
        /// die übrigen trotzdem an die Reihe kommen.
        /// </param>
        public FakeEpisodeImportSource(
            IReadOnlyList<ImportEpisode> episodes,
            string? failForSourceSeriesId = null)
        {
            _episodes = episodes;
            _failForSourceSeriesId = failForSourceSeriesId;
        }

        private readonly string? _failForSourceSeriesId;

        /// <summary>Alle abgefragten Serienkennungen in ihrer Reihenfolge.</summary>
        public List<string> RequestedSeriesIds { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
            string sourceSeriesId,
            IReadOnlySet<string>? knownEpisodeTitles = null,
            CancellationToken cancellationToken = default)
        {
            RequestedSeriesIds.Add(sourceSeriesId);

            if (_failForSourceSeriesId is not null && sourceSeriesId == _failForSourceSeriesId)
            {
                return Task.FromException<IReadOnlyList<ImportEpisode>>(
                    new System.Net.Http.HttpRequestException("Anbieter nicht erreichbar"));
            }

            // knownEpisodeTitles steuert bei echten Quellen nur, ob der teure Track-Lookup
            // (Dauer) entfällt – der zurückgegebene Satz enthält bekannte Alben weiterhin
            // (Metadaten inkl. Cover), damit der Delta-Import Cover nachtragen kann.
            return Task.FromResult(_episodes);
        }
    }
}
