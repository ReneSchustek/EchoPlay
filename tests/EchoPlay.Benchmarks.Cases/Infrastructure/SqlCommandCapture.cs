using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Merkt sich die Abfragen, die EF Core an die Datenbank schickt, samt Parametern. Grundlage des
    /// Berichts der Abfragepläne: Der Plan gehört zu genau dem Befehl, der gemessen wird, nicht zu
    /// einer von Hand nachgeschriebenen Fassung.
    /// </summary>
    internal sealed class SqlCommandCapture : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> _commands = [];

        /// <summary>Die seit dem letzten <see cref="Clear"/> abgeschickten Abfragen.</summary>
        public IReadOnlyList<CapturedCommand> Commands => _commands;

        /// <summary>Vergisst die bisher gemerkten Abfragen.</summary>
        public void Clear() => _commands.Clear();

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands.Add(CapturedCommand.From(command));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        /// <inheritdoc/>
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands.Add(CapturedCommand.From(command));
            return base.ReaderExecuting(command, eventData, result);
        }
    }

    /// <summary>
    /// Eine abgeschickte Abfrage: Befehlstext und Parameter.
    /// </summary>
    /// <param name="CommandText">Der Befehlstext.</param>
    /// <param name="Parameters">Name und Wert jedes Parameters.</param>
    internal sealed record CapturedCommand(string CommandText, IReadOnlyList<KeyValuePair<string, object?>> Parameters)
    {
        /// <summary>Hält Text und Parameter eines Befehls fest.</summary>
        /// <param name="command">Der Befehl.</param>
        /// <returns>Die festgehaltene Abfrage.</returns>
        public static CapturedCommand From(DbCommand command) => new(
            command.CommandText,
            [.. command.Parameters.Cast<DbParameter>().Select(p => new KeyValuePair<string, object?>(p.ParameterName, p.Value))]);
    }
}
