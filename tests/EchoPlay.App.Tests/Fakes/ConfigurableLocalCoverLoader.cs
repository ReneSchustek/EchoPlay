using EchoPlay.LocalLibrary.Cover;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalCoverLoader"/>, der je Ordner liefert, was der Test vorgibt.
    /// Anders als <see cref="FakeLocalCoverLoader"/>, der immer <see langword="null"/> gibt und
    /// damit nur den Fall „kein Cover gefunden" abbildet.
    /// </summary>
    /// <remarks>
    /// Zeichnet zusätzlich auf, mit welchen Argumenten geladen wurde. Daran hängt eine echte
    /// Zusage: Für Serien-Cover wird bewusst kein Titelpfad mitgegeben, weil es dort keinen
    /// Rückgriff auf die Kennzeichnung der Datei gibt.
    /// </remarks>
    internal sealed class ConfigurableLocalCoverLoader : ILocalCoverLoader
    {
        private readonly Dictionary<string, byte[]> _coversByFolder;
        private readonly List<(string? FolderPath, string? FirstTrackPath)> _calls = [];

        /// <param name="coversByFolder">
        /// Ordnerpfad auf Bilddaten. Ordner, die nicht vorkommen, liefern kein Cover.
        /// </param>
        public ConfigurableLocalCoverLoader(Dictionary<string, byte[]>? coversByFolder = null)
        {
            _coversByFolder = coversByFolder ?? [];
        }

        /// <summary>Jeder Ladeversuch mit seinen Argumenten, in Aufrufreihenfolge.</summary>
        public IReadOnlyList<(string? FolderPath, string? FirstTrackPath)> Calls => _calls;

        /// <inheritdoc/>
        public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
        {
            _calls.Add((episodeFolderPath, firstTrackPath));

            if (episodeFolderPath is not null && _coversByFolder.TryGetValue(episodeFolderPath, out byte[]? cover))
            {
                return Task.FromResult<byte[]?>(cover);
            }

            return Task.FromResult<byte[]?>(null);
        }
    }
}
