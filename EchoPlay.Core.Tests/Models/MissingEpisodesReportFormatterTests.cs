using EchoPlay.Core.Models;

namespace EchoPlay.Core.Tests.Models
{
    /// <summary>
    /// Tests für <see cref="MissingEpisodesReportFormatter"/>.
    /// Prüft die korrekte Textformatierung des Fehlende-Folgen-Berichts.
    /// </summary>
    public sealed class MissingEpisodesReportFormatterTests
    {
        [Fact]
        public void FormatAsText_ContainsHeader_WithDate()
        {
            // Überschrift muss Datum enthalten
            MissingEpisodesReport report = BuildReport([]);

            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Fehlende Folgen – EchoPlay", text, StringComparison.Ordinal);
            Assert.Contains("===", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_ShowsLocalGaps_AsFolgeNumbers()
        {
            // Lokale Lücken werden als "Folge 005, Folge 042" angezeigt
            SeriesMissingEpisodesResult result = new()
            {
                SeriesTitle = "TKKG",
                LocalHighestNumber = 210,
                LocalGaps = [5, 42],
                OnlineEpisodes = []
            };

            MissingEpisodesReport report = BuildReport([result]);
            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("TKKG (lokal: 1–210)", text, StringComparison.Ordinal);
            Assert.Contains("Folge 005", text, StringComparison.Ordinal);
            Assert.Contains("Folge 042", text, StringComparison.Ordinal);
            Assert.Contains("Lokale Lücken", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_ShowsOnlineEpisodes_WithTitle()
        {
            // Online verfügbare Folgen werden mit Nummer und Titel angezeigt
            SeriesMissingEpisodesResult result = new()
            {
                SeriesTitle = "Die drei ???",
                LocalHighestNumber = 229,
                OnlineHighestNumber = 231,
                LocalGaps = [],
                OnlineEpisodes =
                [
                    new OnlineEpisodeInfo { EpisodeNumber = 230, Title = "Folge 230" },
                    new OnlineEpisodeInfo { EpisodeNumber = 231, Title = "Folge 231" }
                ]
            };

            MissingEpisodesReport report = BuildReport([result]);
            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Online verfügbar: Folge 230", text, StringComparison.Ordinal);
            Assert.Contains("Online verfügbar: Folge 231", text, StringComparison.Ordinal);
            Assert.Contains("Keine lokalen Lücken", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_ShowsAllComplete_WhenNoGapsAndNoOnline()
        {
            // Keine Lücken, keine Online-Folgen → "Alle Folgen komplett"
            SeriesMissingEpisodesResult result = new()
            {
                SeriesTitle = "Bibi Blocksberg",
                LocalHighestNumber = 145,
                LocalGaps = [],
                OnlineEpisodes = []
            };

            MissingEpisodesReport report = BuildReport([result]);
            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Alle Folgen komplett", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_ShowsSummaryLine()
        {
            // Zusammenfassung mit Serienanzahl und Gesamtzahlen
            SeriesMissingEpisodesResult r1 = new()
            {
                SeriesTitle = "TKKG",
                LocalHighestNumber = 210,
                LocalGaps = [42, 87],
                OnlineEpisodes = [new OnlineEpisodeInfo { EpisodeNumber = 211, Title = "Folge 211" }]
            };
            SeriesMissingEpisodesResult r2 = new()
            {
                SeriesTitle = "Bibi",
                LocalHighestNumber = 145,
                LocalGaps = [],
                OnlineEpisodes = []
            };

            MissingEpisodesReport report = BuildReport([r1, r2]);
            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Geprüft: 2 Serien", text, StringComparison.Ordinal);
            Assert.Contains("Lokale Lücken: 2", text, StringComparison.Ordinal);
            Assert.Contains("Online neu: 1", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_ShowsError_WhenSeriesHasErrorMessage()
        {
            // Fehlermeldung wird angezeigt wenn die Prüfung fehlgeschlagen ist
            SeriesMissingEpisodesResult result = new()
            {
                SeriesTitle = "Kaputte Serie",
                LocalGaps = [],
                OnlineEpisodes = [],
                ErrorMessage = "Ordner nicht lesbar"
            };

            MissingEpisodesReport report = BuildReport([result]);
            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Fehler: Ordner nicht lesbar", text, StringComparison.Ordinal);
        }

        [Fact]
        public void TotalLocalGaps_SumsAllSeriesGaps()
        {
            MissingEpisodesReport report = BuildReport(
            [
                new SeriesMissingEpisodesResult
                {
                    SeriesTitle = "A", LocalHighestNumber = 10,
                    LocalGaps = [3, 7], OnlineEpisodes = []
                },
                new SeriesMissingEpisodesResult
                {
                    SeriesTitle = "B", LocalHighestNumber = 5,
                    LocalGaps = [2], OnlineEpisodes = []
                }
            ]);

            Assert.Equal(3, report.TotalLocalGaps);
        }

        [Fact]
        public void TotalOnlineNew_SumsAllSeriesOnlineEpisodes()
        {
            MissingEpisodesReport report = BuildReport(
            [
                new SeriesMissingEpisodesResult
                {
                    SeriesTitle = "A", LocalHighestNumber = 10,
                    LocalGaps = [],
                    OnlineEpisodes = [new OnlineEpisodeInfo { EpisodeNumber = 11, Title = "F11" }]
                },
                new SeriesMissingEpisodesResult
                {
                    SeriesTitle = "B", LocalHighestNumber = 5,
                    LocalGaps = [],
                    OnlineEpisodes = [
                        new OnlineEpisodeInfo { EpisodeNumber = 6, Title = "F6" },
                        new OnlineEpisodeInfo { EpisodeNumber = 7, Title = "F7" }
                    ]
                }
            ]);

            Assert.Equal(3, report.TotalOnlineNew);
        }

        /// <summary>
        /// Erstellt einen <see cref="MissingEpisodesReport"/> mit festen Ergebnissen.
        /// </summary>
        private static MissingEpisodesReport BuildReport(
            IReadOnlyList<SeriesMissingEpisodesResult> results)
        {
            return new MissingEpisodesReport
            {
                CheckedAtUtc = new DateTime(2026, 3, 31, 14, 0, 0, DateTimeKind.Utc),
                Results = results
            };
        }
    }
}
