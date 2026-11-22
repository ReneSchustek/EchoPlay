using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Core.Logging;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Was das Protokoll an einem vollen Tag zu tun hat: Pfade schwärzen, bevor sie in eine Zeile
    /// kommen, und jede Zeile für die JSON-Datei serialisieren.
    /// </summary>
    /// <remarks>
    /// Die Tagesmenge ist die des vollsten Tages im Protokollordner der Produktivinstallation; ein
    /// gewöhnlicher Tag kommt auf einige hundert bis knapp zweitausend Zeilen. Gemessen wird ohne
    /// Datei: Das Schreiben übernimmt ein Hintergrund-Thread und kostet die Anwendung keine Zeit,
    /// Schwärzen und Serialisieren dagegen laufen auf dem Thread, der protokolliert.
    /// </remarks>
    [MemoryDiagnoser]
    public class LogBenchmarks
    {
        /// <summary>Zeilen am vollsten Tag im Protokollordner (11.11.2026).</summary>
        public const int BusyDayEntries = 23_465;

        private LogEntry[] _entries = [];
        private string[] _paths = [];

        /// <summary>Erzeugt Einträge und Pfade einmal je Messlauf.</summary>
        [GlobalSetup]
        public void CreateDay()
        {
            _entries = LogDay.CreateEntries(BusyDayEntries);
            _paths = LogDay.CreatePaths(BusyDayEntries);
        }

        /// <summary>Schwärzt einen Pfad je Zeile; mehr Pfade hat kein Tag.</summary>
        /// <returns>Gesamtlänge der Ergebnisse, damit nichts wegoptimiert wird.</returns>
        [Benchmark]
        public int RedactPaths()
        {
            int length = 0;
            foreach (string path in _paths)
            {
                length += PathRedactor.Redact(path).Length;
            }

            return length > 0 ? length : throw new InvalidOperationException("Kein Pfad geschwärzt.");
        }

        /// <summary>Serialisiert jede Zeile für die JSON-Datei.</summary>
        /// <returns>Gesamtlänge der Zeilen.</returns>
        [Benchmark]
        public int SerializeJsonLines()
        {
            int length = 0;
            foreach (LogEntry entry in _entries)
            {
                length += JsonLogSink.Serialize(entry).Length;
            }

            return length > 0 ? length : throw new InvalidOperationException("Keine Zeile serialisiert.");
        }
    }
}
