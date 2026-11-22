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
            // Eingerückt ist die Zeile im Serienblock, die Fußzeile nennt die Lücken ohne Einzug.
            Assert.DoesNotContain("  Lokale Lücken:", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_CompleteSeries_IsLeftOut()
        {
            // Der Bericht heißt „Fehlende Folgen": Eine vollständige Serie hat darin nichts zu suchen.
            MissingEpisodesReport report = BuildReport([CompleteSeries("Bibi Blocksberg"), SeriesWithGap("TKKG")]);

            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.DoesNotContain("Bibi Blocksberg", text, StringComparison.Ordinal);
            Assert.Contains("TKKG (lokal: 1–210)", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_SeriesWithoutLocalAndOnlineEpisodes_IsLeftOut()
        {
            SeriesMissingEpisodesResult empty = new()
            {
                SeriesTitle = "Leere Serie",
                LocalGaps = [],
                OnlineEpisodes = []
            };

            string text = MissingEpisodesReportFormatter.FormatAsText(BuildReport([empty, SeriesWithGap("TKKG")]));

            Assert.DoesNotContain("Leere Serie", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_NothingMissingAnywhere_SaysSo()
        {
            MissingEpisodesReport report = BuildReport([CompleteSeries("Bibi Blocksberg"), CompleteSeries("Benjamin Blümchen")]);

            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Keine fehlenden Folgen.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bibi Blocksberg", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Benjamin Blümchen", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_WithFinding_DoesNotSayNothingIsMissing()
        {
            string text = MissingEpisodesReportFormatter.FormatAsText(BuildReport([SeriesWithGap("TKKG")]));

            Assert.DoesNotContain("Keine fehlenden Folgen", text, StringComparison.Ordinal);
        }

        [Fact]
        public void FormatAsText_FooterCountsLeftOutSeriesToo()
        {
            // Die Fußzeile ist der einzige Hinweis darauf, dass die ausgeblendeten Serien mitgeprüft wurden.
            MissingEpisodesReport report = BuildReport(
                [CompleteSeries("Bibi Blocksberg"), CompleteSeries("Benjamin Blümchen"), SeriesWithGap("TKKG")]);

            string text = MissingEpisodesReportFormatter.FormatAsText(report);

            Assert.Contains("Geprüft: 3 Serien", text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, false, false, true)]
        [InlineData(false, true, false, true)]
        [InlineData(false, false, true, true)]
        public void HasFinding_ReflectsGapOnlineEpisodeOrError(bool hasGap, bool hasOnline, bool hasError, bool expected)
        {
            SeriesMissingEpisodesResult result = new()
            {
                SeriesTitle = "Serie",
                LocalHighestNumber = 10,
                LocalGaps = hasGap ? [4] : [],
                OnlineEpisodes = hasOnline ? [new OnlineEpisodeInfo { EpisodeNumber = 11, Title = "F11" }] : [],
                ErrorMessage = hasError ? "Ordner nicht lesbar" : null
            };

            Assert.Equal(expected, result.HasFinding);
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

        private static SeriesMissingEpisodesResult CompleteSeries(string title) => new()
        {
            SeriesTitle = title,
            LocalHighestNumber = 145,
            LocalGaps = [],
            OnlineEpisodes = []
        };

        private static SeriesMissingEpisodesResult SeriesWithGap(string title) => new()
        {
            SeriesTitle = title,
            LocalHighestNumber = 210,
            LocalGaps = [42],
            OnlineEpisodes = []
        };

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
