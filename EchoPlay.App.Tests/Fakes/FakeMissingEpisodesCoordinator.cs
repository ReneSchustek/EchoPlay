using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IMissingEpisodesCoordinator"/>. Spiegelt die Verzweigungen
    /// des echten Coordinators wider, die für die VM-Tests relevant sind: Cancel,
    /// fehlender/nicht existierender Ordner und ein Standard-Erfolgsfall.
    /// </summary>
    internal sealed class FakeMissingEpisodesCoordinator : IMissingEpisodesCoordinator
    {
        /// <summary>Aufgezeichnete Aufrufe der Einzelserien-Prüfung.</summary>
        public List<(Guid SeriesId, string? FolderPath, MissingEpisodesMode Mode)> SingleCalls { get; } = [];

        /// <summary>Aufgezeichnete Aufrufe der Gesamtprüfung.</summary>
        public List<MissingEpisodesMode> AllCalls { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<string>> CheckSingleSeriesAsync(
            Guid seriesId,
            string? seriesFolderPath,
            MissingEpisodesMode mode)
        {
            SingleCalls.Add((seriesId, seriesFolderPath, mode));

            if (mode == MissingEpisodesMode.Cancel)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return Task.FromResult<IReadOnlyList<string>>(
                    ["Kein lokaler Ordner für diese Serie vorhanden."]);
            }

            return Task.FromResult<IReadOnlyList<string>>(["Alle Folgen vorhanden."]);
        }

        /// <inheritdoc/>
        public Task<MissingEpisodesReport> CheckAllSeriesAsync(MissingEpisodesMode mode)
        {
            AllCalls.Add(mode);
            return Task.FromResult(new MissingEpisodesReport
            {
                CheckedAtUtc = TestIds.ReferenceDate,
                Results      = []
            });
        }
    }
}
