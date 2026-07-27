using EchoPlay.Core.Scoring;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Verwaltung der Merkliste überwachter Serientitel.
    /// </summary>
    /// <remarks>
    /// Initialisiert eine neue Instanz des <see cref="WatchedTitleDataService"/>.
    /// <para>
    /// <see cref="SyncFromWatchedSeriesAsync"/> liest die <c>Series</c>-Tabelle mit — lesend und
    /// nur auf den Titel projiziert. Der Abgleich ist per Definition „leite die Merkliste aus den
    /// überwachten Serien ab"; ihn aufzuteilen würde die Regel über zwei Klassen verstreuen.
    /// </para>
    /// </remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class WatchedTitleDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IWatchedTitleDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("WatchedTitleDataService");

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlySet<string>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            List<string> titles = await _context.WatchedTitles
                .Select(w => w.NormalizedTitle)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new HashSet<string>(titles, StringComparer.Ordinal);
        }

        /// <inheritdoc/>
        /// <param name="title">Der Serientitel in Originalschreibweise.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task RememberAsync(string title, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            string normalized = HoerspielTextNormalizer.Normalize(title);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            bool exists = await _context.WatchedTitles
                .AnyAsync(w => w.NormalizedTitle == normalized, cancellationToken).ConfigureAwait(false);

            if (exists)
            {
                return;
            }

            _ = _context.WatchedTitles.Add(new WatchedTitle
            {
                Title = title,
                NormalizedTitle = normalized
            });

            // Zwischen Prüfung und Insert kann ein paralleler Scope denselben Titel angelegt haben
            // (Startup-Abgleich und Favoriten-Klick laufen unabhängig voneinander). Der UNIQUE-Index
            // fängt das ab; der Konflikt ist hier kein Fehler, sondern das gewünschte Ergebnis.
            DbUpdateException? conflict = await _context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                _logger.Debug(() => $"Titel '{normalized}' war bereits gemerkt (paralleler Schreibzugriff).");
            }
        }

        /// <inheritdoc/>
        /// <param name="title">Der Serientitel in Originalschreibweise.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task ForgetAsync(string title, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            string normalized = HoerspielTextNormalizer.Normalize(title);

            // Ohne diese Prüfung würde ein Titel, von dem die Normalisierung nichts übrig lässt
            // (etwa „???"), alle Einträge mit leerem Vergleichstitel auf einmal löschen.
            // RememberAsync legt solche Zeilen zwar nie an — verlassen wollen wir uns darauf
            // beim Löschen aber nicht.
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            _ = await _context.WatchedTitles
                .Where(w => w.NormalizedTitle == normalized)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> SyncFromWatchedSeriesAsync(CancellationToken cancellationToken = default)
        {
            List<string> watchedSeriesTitles = await _context.Series
                .Where(s => s.IsWatched)
                .Select(s => s.Title)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (watchedSeriesTitles.Count == 0)
            {
                return 0;
            }

            HashSet<string> known = new(
                await _context.WatchedTitles
                    .Select(w => w.NormalizedTitle)
                    .ToListAsync(cancellationToken).ConfigureAwait(false),
                StringComparer.Ordinal);

            List<WatchedTitle> missing = [];

            foreach (string title in watchedSeriesTitles)
            {
                string normalized = HoerspielTextNormalizer.Normalize(title);

                // known dient zugleich als Dedup innerhalb dieses Laufs – die Prod-DB
                // enthält Serien-Duplikate mit identischem Titel.
                if (string.IsNullOrWhiteSpace(normalized) || !known.Add(normalized))
                {
                    continue;
                }

                missing.Add(new WatchedTitle { Title = title, NormalizedTitle = normalized });
            }

            if (missing.Count == 0)
            {
                return 0;
            }

            _context.WatchedTitles.AddRange(missing);

            // Gleiche Begründung wie in RememberAsync: ein parallel laufender
            // Favoriten-Klick kann denselben Titel bereits eingetragen haben.
            DbUpdateException? conflict = await _context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                _logger.Debug(() => $"Merklisten-Abgleich traf auf bereits vorhandene Titel: {conflict.InnerException?.Message}");
                return 0;
            }

            _logger.Info("{Count} überwachte Serientitel in die Merkliste übernommen.", missing.Count);
            return missing.Count;
        }
    }
}
