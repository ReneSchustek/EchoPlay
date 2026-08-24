using EchoPlay.App.Models;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IDialogSuppressionService"/>.
    /// Singleton-Dienst: legt sich für jeden Datenbankzugriff einen eigenen Bereich an,
    /// weil <see cref="IDialogSuppressionDataService"/> an den DbContext gebunden ist.
    /// </summary>
    /// <remarks>
    /// Die Menge der ausgeblendeten Schlüssel wird einmal gelesen und danach im Speicher
    /// gehalten. Ohne das käme jede Rückfrage und jeder Seitenwechsel eine Datenbankrunde.
    /// </remarks>
    public sealed class DialogSuppressionService : IDialogSuppressionService, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private HashSet<DialogKey>? _suppressed;

        /// <summary>
        /// Initialisiert den Dienst mit der Bereichsfabrik der Anwendung.
        /// </summary>
        /// <param name="scopeFactory">Bereichsfabrik für die datenbanknahen Zugriffe.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Protokollierers.</param>
        public DialogSuppressionService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("DialogSuppressionService");
        }

        /// <inheritdoc/>
        /// <param name="key">Der zu prüfende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> IsSuppressedAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            if (key == DialogKey.None)
            {
                return false;
            }

            HashSet<DialogKey>? keys = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
            return keys is not null && keys.Contains(key);
        }

        /// <inheritdoc/>
        /// <param name="key">Der auszublendende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Das Häkchen ist eine Bequemlichkeit. Scheitert das Speichern (gesperrte Datei, voller Datenträger), darf die auslösende Nutzeraktion daran nicht scheitern — der Dialog erscheint dann eben weiterhin.")]
        public async Task SuppressAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            if (key == DialogKey.None)
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDialogSuppressionDataService dataService =
                    scope.ServiceProvider.GetRequiredService<IDialogSuppressionDataService>();
                await dataService.SuppressAsync(key.ToString(), cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning("Dialog {Key} konnte nicht ausgeblendet werden: {Reason}", key, ex.Message);
                return;
            }

            await MutateCacheAsync(set => set.Add(key), cancellationToken).ConfigureAwait(true);
        }

        /// <inheritdoc/>
        /// <param name="key">Der zurückzuholende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task RestoreAsync(DialogKey key, CancellationToken cancellationToken = default)
        {
            if (key == DialogKey.None)
            {
                return;
            }

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IDialogSuppressionDataService dataService =
                    scope.ServiceProvider.GetRequiredService<IDialogSuppressionDataService>();
                await dataService.RestoreAsync(key.ToString(), cancellationToken).ConfigureAwait(true);
            }

            await MutateCacheAsync(set => set.Remove(key), cancellationToken).ConfigureAwait(true);
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task RestoreAllAsync(CancellationToken cancellationToken = default)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IDialogSuppressionDataService dataService =
                    scope.ServiceProvider.GetRequiredService<IDialogSuppressionDataService>();
                _ = await dataService.RestoreAllAsync(cancellationToken).ConfigureAwait(true);
            }

            await MutateCacheAsync(static set => set.Clear(), cancellationToken).ConfigureAwait(true);
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<SuppressedDialogInfo>> GetSuppressedAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IDialogSuppressionDataService dataService =
                scope.ServiceProvider.GetRequiredService<IDialogSuppressionDataService>();
            IReadOnlyList<DialogSuppression> rows = await dataService.GetAllAsync(cancellationToken).ConfigureAwait(true);

            List<SuppressedDialogInfo> result = new(rows.Count);
            foreach (DialogSuppression row in rows)
            {
                // Ein Schlüssel aus einer früheren Fassung, den es nicht mehr gibt, wird
                // übergangen statt gemeldet — die Zeile schadet niemandem und verschwindet
                // mit der nächsten Bereinigung.
                if (Enum.TryParse(row.Key, out DialogKey key) && key != DialogKey.None)
                {
                    result.Add(new SuppressedDialogInfo(key, row.CreatedAt));
                }
            }

            return result;
        }

        /// <summary>Gibt die interne Sperre frei.</summary>
        public void Dispose() => _gate.Dispose();

        /// <summary>
        /// Liest die ausgeblendeten Schlüssel beim ersten Zugriff und hält sie danach im Speicher.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Menge der Schlüssel; <see langword="null"/>, wenn sie nicht lesbar war.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Vor der ersten Migration oder bei gesperrter Datenbank ist die Tabelle nicht lesbar. Der Dialog soll dann erscheinen — ein nicht lesbarer Merkzettel darf keinen Hinweis verschlucken und erst recht keine Rückfrage überspringen.")]
        private async Task<HashSet<DialogKey>?> EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            // Volatile.Read statt des Feldes selbst: Der Zwischenspeicher wird aus mehreren
            // Abläufen gelesen, und die zweite Prüfung hinter der Sperre ist genau die, die
            // ein paralleles Laden verhindert.
            HashSet<DialogKey>? cached = Volatile.Read(ref _suppressed);
            if (cached is not null)
            {
                return cached;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                cached = Volatile.Read(ref _suppressed);
                if (cached is not null)
                {
                    return cached;
                }

                IReadOnlyList<SuppressedDialogInfo> rows = await GetSuppressedAsync(cancellationToken).ConfigureAwait(true);

                HashSet<DialogKey> keys = [];
                foreach (SuppressedDialogInfo row in rows)
                {
                    _ = keys.Add(row.Key);
                }

                Volatile.Write(ref _suppressed, keys);
                return keys;
            }
            catch (Exception ex)
            {
                // Bewusst nicht zwischenspeichern: Beim nächsten Dialog wird erneut versucht.
                _logger.Warning("Ausgeblendete Dialoge nicht lesbar: {Reason}", ex.Message);
                return null;
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        /// <summary>
        /// Wendet eine Änderung auf den Zwischenspeicher an, sofern er bereits steht.
        /// </summary>
        /// <param name="change">Die Änderung an der Menge.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        private async Task MutateCacheAsync(Action<HashSet<DialogKey>> change, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                HashSet<DialogKey>? cached = Volatile.Read(ref _suppressed);
                if (cached is not null)
                {
                    change(cached);
                }
            }
            finally
            {
                _ = _gate.Release();
            }
        }
    }
}
