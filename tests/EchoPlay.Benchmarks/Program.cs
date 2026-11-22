using BenchmarkDotNet.Running;
using EchoPlay.Benchmarks.Cases.Benchmarks;
using EchoPlay.Benchmarks.Cases.Reports;

namespace EchoPlay.Benchmarks
{
    /// <summary>
    /// Einstieg des Messprojekts. Ohne Argument fragt BenchmarkDotNet, welche Messklasse laufen
    /// soll; <c>--filter *</c> misst alle. <c>--plans &lt;Datei&gt;</c> misst nichts, sondern schreibt
    /// die Abfragepläne der gemessenen Abfragen.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--plans")
            {
                StreamWriter output = new(args[1]);
                await using (output.ConfigureAwait(false))
                {
                    await QueryPlanReport.WriteAsync(output).ConfigureAwait(false);
                }

                return;
            }

            _ = BenchmarkSwitcher.FromAssembly(typeof(LibraryQueryBenchmarks).Assembly).Run(args);
        }
    }
}
