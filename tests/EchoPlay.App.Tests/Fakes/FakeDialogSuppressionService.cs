using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IDialogSuppressionService"/>. Hält die ausgeblendeten Dialoge
    /// im Speicher und zählt die Zugriffe, damit Tests ohne Datenbank auskommen.
    /// </summary>
    internal sealed class FakeDialogSuppressionService : IDialogSuppressionService
    {
        private readonly Dictionary<DialogKey, DateTime> _suppressed = [];

        /// <summary>Feste Zeit, die als Zeitpunkt des Ausblendens eingetragen wird.</summary>
        public DateTime Now { get; set; } = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>Wie oft nach dem Zustand gefragt wurde.</summary>
        public int IsSuppressedCallCount { get; private set; }

        /// <summary>Wie oft etwas ausgeblendet wurde.</summary>
        public int SuppressCallCount { get; private set; }

        /// <summary>Wie oft ein einzelner Hinweis zurückgeholt wurde.</summary>
        public int RestoreCallCount { get; private set; }

        /// <summary>Wie oft alles zurückgeholt wurde.</summary>
        public int RestoreAllCallCount { get; private set; }

        /// <summary>Trägt einen Dialog als ausgeblendet ein, ohne die Zähler zu bewegen.</summary>
        /// <param name="key">Der auszublendende Dialog.</param>
        public void Preset(DialogKey key) => _suppressed[key] = Now;

        /// <inheritdoc/>
        public Task<bool> IsSuppressedAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            IsSuppressedCallCount++;
            return Task.FromResult(_suppressed.ContainsKey(key));
        }

        /// <inheritdoc/>
        public Task SuppressAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            SuppressCallCount++;
            _suppressed[key] = Now;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RestoreAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            RestoreCallCount++;
            _ = _suppressed.Remove(key);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RestoreAllAsync(CancellationToken cancellationToken = default)
        {
            RestoreAllCallCount++;
            _suppressed.Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SuppressedDialogInfo>> GetSuppressedAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SuppressedDialogInfo> result = _suppressed
                .Select(entry => new SuppressedDialogInfo(entry.Key, entry.Value))
                .OrderByDescending(info => info.SuppressedAt)
                .ThenBy(info => info.Key)
                .ToList();

            return Task.FromResult(result);
        }
    }
}
