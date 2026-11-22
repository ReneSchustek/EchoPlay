using EchoPlay.Core.Replication;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// Verschiebt alle gespeicherten Pfade der Mediathek von einem Stammpfad auf einen anderen, in
    /// einem Vorgang.
    /// </summary>
    /// <remarks>
    /// Die Datenbank speichert absolute Pfade. Liegt die Mediathek nach einem Umzug oder dem
    /// Entpacken woanders, stimmt keiner mehr. Statt jeden Pfad relativ zu speichern und an jeder
    /// Stelle aufzulösen, verschiebt dieser Dienst die Pfade einmal, wenn sich der Stamm ändert.
    /// Pfade außerhalb des alten Stamms bleiben, wie sie sind; sie werden gezählt, damit der
    /// Aufrufer sie nennen kann. Alles läuft in einer Transaktion: Entweder stimmen danach alle
    /// Pfade oder keiner hat sich geändert.
    /// </remarks>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Fabrik.</param>
    public sealed class LibraryRootRebaser(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ILibraryRootRebaser
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("LibraryRootRebaser");

        /// <inheritdoc/>
        public async Task<LibraryRebaseResult> RebaseAsync(string oldRoot, string newRoot, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(oldRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);

            if (!Path.IsPathFullyQualified(oldRoot) || !Path.IsPathFullyQualified(newRoot))
            {
                throw new ArgumentException("Beide Stammpfade müssen absolut sein.", nameof(newRoot));
            }

            IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Counter counter = new();
                await RebaseTrackedPathsAsync(counter, oldRoot, newRoot, cancellationToken).ConfigureAwait(false);
                await SetLibraryRootAsync(newRoot, cancellationToken).ConfigureAwait(false);

                _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                LibraryRebaseResult result = new(counter.Rebased, counter.OutsideRoot);
                _logger.Info(
                    "Stammpfad verschoben: {Rebased} Pfade umgerechnet, {Outside} außerhalb des alten Stamms unverändert.",
                    result.Rebased, result.OutsideRoot);
                return result;
            }
            finally
            {
                _context.ChangeTracker.Clear();
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lädt Serien, Folgen und Spuren verfolgt und rechnet ihre Pfade um, noch ohne zu speichern.
        /// Gelöschte Zeilen gehören dazu: Stellt jemand sie wieder her, müssen auch ihre Pfade stimmen.
        /// </summary>
        private async Task RebaseTrackedPathsAsync(Counter counter, string oldRoot, string newRoot, CancellationToken cancellationToken)
        {
            List<Series> series = await _context.Series
                .AsTracking()
                .IgnoreQueryFilters()
                .Where(s => s.LocalFolderPath != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (Series item in series)
            {
                item.LocalFolderPath = counter.Rebase(oldRoot, newRoot, item.LocalFolderPath!);
            }

            List<Episode> episodes = await _context.Episodes
                .AsTracking()
                .IgnoreQueryFilters()
                .Where(e => e.LocalFolderPath != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (Episode episode in episodes)
            {
                episode.LocalFolderPath = counter.Rebase(oldRoot, newRoot, episode.LocalFolderPath!);
            }

            await RebaseTrackPathsAsync(counter, oldRoot, newRoot, cancellationToken).ConfigureAwait(false);
        }

        private async Task RebaseTrackPathsAsync(Counter counter, string oldRoot, string newRoot, CancellationToken cancellationToken)
        {
            List<LocalTrack> tracks = await _context.LocalTracks
                .AsTracking()
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (LocalTrack track in tracks)
            {
                track.FilePath = counter.Rebase(oldRoot, newRoot, track.FilePath);
            }
        }

        /// <summary>Trägt den neuen Stammpfad in die Einstellungen ein, noch ohne zu speichern.</summary>
        private async Task SetLibraryRootAsync(string newRoot, CancellationToken cancellationToken)
        {
            AppSettings? settings = await _context.AppSettings.AsTracking()
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings is not null)
            {
                settings.LocalLibraryRootPath = newRoot;
            }
        }

        /// <summary>Rechnet einen Pfad um und zählt dabei mit.</summary>
        private sealed class Counter
        {
            /// <summary>Zahl der umgerechneten Pfade.</summary>
            public int Rebased { get; private set; }

            /// <summary>Zahl der Pfade außerhalb des alten Stamms.</summary>
            public int OutsideRoot { get; private set; }

            /// <summary>Der Pfad unter dem neuen Stamm, oder unverändert, wenn er nicht unter dem alten lag.</summary>
            public string Rebase(string oldRoot, string newRoot, string path)
            {
                string? moved = LibraryPathMapper.Rebase(oldRoot, newRoot, path);
                if (moved is null)
                {
                    OutsideRoot++;
                    return path;
                }

                Rebased++;
                return moved;
            }
        }
    }
}
