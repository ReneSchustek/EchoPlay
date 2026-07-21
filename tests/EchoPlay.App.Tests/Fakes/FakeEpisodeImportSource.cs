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
        public FakeEpisodeImportSource(IReadOnlyList<ImportEpisode> episodes)
        {
            _episodes = episodes;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<ImportEpisode>> GetEpisodesAsync(
            string sourceSeriesId,
            IReadOnlySet<string>? knownEpisodeTitles = null,
            CancellationToken cancellationToken = default)
        {
            // knownEpisodeTitles steuert bei echten Quellen nur, ob der teure Track-Lookup
            // (Dauer) entfällt – der zurückgegebene Satz enthält bekannte Alben weiterhin
            // (Metadaten inkl. Cover), damit der Delta-Import Cover nachtragen kann.
            return Task.FromResult(_episodes);
        }
    }
}
