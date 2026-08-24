using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IDialogSuppressionDataService"/>. Hält die Schlüssel als reine
    /// Liste und zählt die Lesezugriffe — daran hängt der Nachweis, dass der App-Dienst
    /// wirklich zwischenspeichert.
    /// </summary>
    internal sealed class FakeDialogSuppressionDataService : IDialogSuppressionDataService
    {
        private readonly List<DialogSuppression> _rows = [];

        /// <summary>Wie oft die Liste gelesen wurde.</summary>
        public int GetAllCallCount { get; private set; }

        /// <summary>Wenn gesetzt, scheitert jeder Lesezugriff mit dieser Ausnahme.</summary>
        public Exception? ReadFailure { get; set; }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DialogSuppression>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCallCount++;

            if (ReadFailure is not null)
            {
                return Task.FromException<IReadOnlyList<DialogSuppression>>(ReadFailure);
            }

            return Task.FromResult<IReadOnlyList<DialogSuppression>>(_rows.AsReadOnly());
        }

        /// <inheritdoc/>
        public Task SuppressAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_rows.Exists(row => row.Key == key))
            {
                _rows.Add(new DialogSuppression { Key = key });
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RestoreAsync(string key, CancellationToken cancellationToken = default)
        {
            _ = _rows.RemoveAll(row => row.Key == key);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<int> RestoreAllAsync(CancellationToken cancellationToken = default)
        {
            int count = _rows.Count;
            _rows.Clear();
            return Task.FromResult(count);
        }
    }
}
