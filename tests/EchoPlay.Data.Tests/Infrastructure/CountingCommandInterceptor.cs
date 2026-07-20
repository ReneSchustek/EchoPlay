using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Zählt ausgeführte SELECT-Kommandos pro Test.
    /// Ermöglicht Akzeptanztests, die belegen, dass eine Aggregation tatsächlich
    /// in einem einzigen Roundtrip beantwortet wird (kein N+1, keine Client-Evaluation
    /// mit nachgelagertem Materialisierungs-Query).
    /// </summary>
    [SuppressMessage("Design", "CA1515:Consider making public types internal",
        Justification = "Wird in public xUnit-Test-Klassen als Konstruktorparameter exponiert und muss daher public sein.")]
    public sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        /// <summary>
        /// Anzahl bisher ausgeführter SELECT-Kommandos.
        /// </summary>
        public int SelectCount { get; private set; }

        /// <summary>
        /// Setzt den Zähler zurück. Nützlich, um nach Setup-Persistenz nur die zu prüfende Methode zu messen.
        /// </summary>
        public void Reset()
        {
            SelectCount = 0;
        }

        /// <inheritdoc/>
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            System.ArgumentNullException.ThrowIfNull(command);
            Track(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            System.ArgumentNullException.ThrowIfNull(command);
            Track(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Track(DbCommand command)
        {
            // SQLite-EF-Provider sendet die Anweisung exakt so, wie sie ausgeführt wird –
            // führende Whitespaces sind nicht garantiert, deshalb TrimStart vor StartsWith.
            if (command.CommandText.TrimStart().StartsWith("SELECT", System.StringComparison.OrdinalIgnoreCase))
            {
                SelectCount++;
            }
        }
    }
}
