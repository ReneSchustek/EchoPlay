using EchoPlay.Logger.Models;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Ein Protokolltag aus festem Startwert: Einträge in der Mischung der Produktivinstallation und
    /// Pfade in der Form der Mediathek.
    /// </summary>
    internal static class LogDay
    {
        /// <summary>Startwert; ändert er sich, ist die Messreihe gebrochen.</summary>
        public const int Seed = 20261122;

        // Kategorien und Texte in der Art des Tagesprotokolls; die Länge zählt, nicht der Wortlaut.
        private static readonly string[] Categories = ["LocalLibraryScanner", "BackgroundCoverService", "HttpRequestLoggingHandler", "PlayerService", "ImportService", "SearchViewModel"];
        private static readonly string[] Messages =
        [
            "Ordner eingelesen: {0} Folgen, {1} Spuren",
            "Cover nachgeladen für Folge {0}, {1} KB",
            "GET https://itunes.apple.com/search?term=*** → 200 in {0} ms",
            "Wiedergabe gestartet: Spur {0} von {1}",
            "Serie abgeglichen: {0} neue Folgen, {1} unverändert",
            "Suche abgeschlossen: {0} Treffer in {1} ms"
        ];

        /// <summary>
        /// Erzeugt die Einträge eines Tages, etwa jeder fünfzigste mit Ausnahme.
        /// </summary>
        /// <param name="count">Zahl der Einträge.</param>
        /// <returns>Die Einträge.</returns>
        public static LogEntry[] CreateEntries(int count)
        {
            SeededSequence random = new(Seed);
            DateTime start = new(2026, 11, 11, 6, 0, 0, DateTimeKind.Utc);
            InvalidOperationException failure = CreateFailure();

            LogEntry[] entries = new LogEntry[count];
            for (int i = 0; i < count; i++)
            {
                int kind = random.Next(Messages.Length);
                string message = string.Format(System.Globalization.CultureInfo.InvariantCulture, Messages[kind], random.Next(400), random.Next(2000));
                bool failed = random.Next(50) == 0;
                entries[i] = new LogEntry(
                    start.AddMilliseconds(i * 2_000L),
                    failed ? LogLevel.Error : (LogLevel)random.Next((int)LogLevel.Warning + 1),
                    message,
                    Categories[kind],
                    [$"UserAction:{random.Next(1000)}", $"API:Provider:{Categories[kind]}"],
                    failed ? failure : null);
            }

            return entries;
        }

        /// <summary>
        /// Erzeugt Pfade in der Form der Mediathek, verteilt über die Serien des Messbestands.
        /// </summary>
        /// <param name="count">Zahl der Pfade.</param>
        /// <returns>Die Pfade.</returns>
        public static string[] CreatePaths(int count)
        {
            SeededSequence random = new(Seed + 1);
            string[] paths = new string[count];
            for (int i = 0; i < count; i++)
            {
                int series = random.Next(LibraryShape.SeriesCount);
                int episode = 1 + random.Next(LibraryShape.LargestSeriesEpisodes);
                paths[i] = $@"D:\Mp3\Hörspiele\Messserie {series:000}\{episode:000} - Messfolge\{1 + random.Next(4):00}.mp3";
            }

            return paths;
        }

        /// <summary>Eine geworfene Ausnahme, damit sie einen Stacktrace trägt wie im Betrieb.</summary>
        private static InvalidOperationException CreateFailure()
        {
            try
            {
                throw new InvalidOperationException("Messfehler: Die Gegenstelle hat nicht geantwortet.");
            }
            catch (InvalidOperationException ex)
            {
                return ex;
            }
        }
    }
}
