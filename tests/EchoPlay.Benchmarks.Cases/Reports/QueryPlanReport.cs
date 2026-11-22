using System.Reflection;
using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Benchmarks;
using EchoPlay.Benchmarks.Cases.Infrastructure;

namespace EchoPlay.Benchmarks.Cases.Reports
{
    /// <summary>
    /// Schreibt zu jeder gemessenen Abfrage den Plan, den SQLite für sie wählt (wie
    /// <c>EXPLAIN QUERY PLAN</c>, siehe <see cref="SqliteExplain"/>), als Markdown.
    /// </summary>
    /// <remarks>
    /// Der Plan ist ein Beleg, kein Beweis. Er sagt, ob ein Index benutzt wird, nicht, wie lange die
    /// Abfrage dauert; das sagt nur die Messung. Steht neben einer schnellen Zeit ein
    /// <c>SCAN</c>, ist das kein Fehler, solange die Tabelle klein ist. Steht neben einer langsamen
    /// Zeit ein <c>SCAN</c> über eine große Tabelle, weiß man, wo man suchen muss.
    /// <para>
    /// Die Abfragen werden nicht nachgeschrieben, sondern beim Ausführen der Messmethoden
    /// mitgeschnitten (<see cref="SqlCommandCapture"/>). So gehört jeder Plan zu genau dem Befehl,
    /// den EF Core in der gemessenen Fassung erzeugt.
    /// </para>
    /// </remarks>
    public static class QueryPlanReport
    {
        /// <summary>
        /// Legt die Messdatenbanken an, führt jede Messmethode mit Datenbankzugriff einmal aus und
        /// schreibt die Pläne ihrer Abfragen.
        /// </summary>
        /// <param name="output">Ziel des Berichts.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Bericht geschrieben ist.</returns>
        public static async Task WriteAsync(TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(output);

            SqlCommandCapture libraryCapture = new();
            LibraryQueryBenchmarks library = new() { Capture = libraryCapture };
            library.CreateDatabase();
            try
            {
                await WriteClassAsync(output, library, libraryCapture, library.DatabasePath).ConfigureAwait(false);
            }
            finally
            {
                library.DeleteDatabase();
            }

            SqlCommandCapture coverCapture = new();
            CoverBenchmarks covers = new() { Capture = coverCapture };
            covers.CreateDatabase();
            try
            {
                await output.WriteLineAsync(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Cover im Messbestand: {CoverSeed.CoverEdge} × {CoverSeed.CoverEdge} Pixel, JPEG, im Mittel {covers.MeanCoverBytes / 1024:F0} KB.")).ConfigureAwait(false);
                await output.WriteLineAsync().ConfigureAwait(false);
                await WriteClassAsync(output, covers, coverCapture, covers.DatabasePath).ConfigureAwait(false);
            }
            finally
            {
                covers.DeleteDatabase();
            }
        }

        /// <summary>Schreibt eine Abfrage und ihren Plan als zwei Codeblöcke.</summary>
        private static async Task WriteCommandAsync(TextWriter output, CapturedCommand command, string databasePath)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync("```sql").ConfigureAwait(false);
            await output.WriteLineAsync(command.CommandText.Trim()).ConfigureAwait(false);
            await output.WriteLineAsync("```").ConfigureAwait(false);
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync("```text").ConfigureAwait(false);
            await output.WriteLineAsync(SqliteExplain.QueryPlan(databasePath, command)).ConfigureAwait(false);
            await output.WriteLineAsync("```").ConfigureAwait(false);
        }

        private static async Task WriteClassAsync(TextWriter output, object benchmarks, SqlCommandCapture capture, string databasePath)
        {
            await output.WriteLineAsync($"## {benchmarks.GetType().Name}").ConfigureAwait(false);
            await output.WriteLineAsync().ConfigureAwait(false);

            IEnumerable<MethodInfo> methods = benchmarks.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() is not null)
                .OrderBy(m => m.Name, StringComparer.Ordinal);

            foreach (MethodInfo method in methods)
            {
                capture.Clear();
                object? result = method.Invoke(benchmarks, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }

                // Messmethoden ohne Datenbankzugriff, etwa das Dekodieren, haben keinen Plan.
                if (capture.Commands.Count == 0)
                {
                    continue;
                }

                await output.WriteLineAsync($"### {method.Name}").ConfigureAwait(false);
                foreach (CapturedCommand command in capture.Commands)
                {
                    await WriteCommandAsync(output, command, databasePath).ConfigureAwait(false);
                }

                await output.WriteLineAsync().ConfigureAwait(false);
            }
        }
    }
}
