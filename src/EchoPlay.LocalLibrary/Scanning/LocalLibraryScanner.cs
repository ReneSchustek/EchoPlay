using EchoPlay.Core.Logging;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Durchsucht eine lokale Verzeichnisstruktur nach Hörspielserien und Episoden.
    /// Unterstützt drei Hierarchieebenen: Root → Serienordner → Episodenordner → Audiodateien.
    /// Alternativ auch flache Struktur: Root → Serienordner → direkte Audiodateien.
    /// IO-Fehler auf Ordnerebene werden geloggt und übersprungen, kein Absturz
    /// (<see cref="SafeDirectoryReader"/>).
    /// </summary>
    public sealed class LocalLibraryScanner : ILocalLibraryScanner
    {
        private readonly ILogger _logger;
        private readonly SafeDirectoryReader _directories;

        /// <summary>
        /// Initialisiert den Scanner.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public LocalLibraryScanner(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger(nameof(LocalLibraryScanner));
            _directories = new SafeDirectoryReader(_logger);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetSeriesFolders(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return [];
            }

            return _directories.GetSubdirectories(rootPath);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Vor der Schleife zählt der Scanner alle Audiodateien, damit der Fortschritt eine
        /// Prozentangabe hat. Ordner, deren Name nicht zum Muster passt, werden trotzdem
        /// eingelesen; die Nummer kommt dann aus den allgemeinen Mustern oder fehlt.
        /// </remarks>
        public Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress = null,
            IProgress<LocalScanResult>? onSeriesScanned = null,
            CancellationToken ct = default)
        {
            // Der Dateizugriff ist synchron und liefe sonst auf dem UI-Thread. Das Token geht an
            // Task.Run, damit ein früher Abbruch den Lauf gar nicht erst startet, und in die
            // Schleife, damit ein laufender je Serie endet.
            return Task.Run<IReadOnlyList<LocalScanResult>>(
                () => ExecuteScan(rootPath, folderPattern, progress, onSeriesScanned, ct), ct);
        }

        /// <summary>
        /// Führt den eigentlichen synchronen Scan-Vorgang durch.
        /// Wird von <see cref="ScanSeriesAsync"/> auf einem Threadpool-Thread gestartet.
        /// </summary>
        private List<LocalScanResult> ExecuteScan(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress,
            IProgress<LocalScanResult>? onSeriesScanned,
            CancellationToken ct)
        {
            using LogScope scope = _logger.BeginScope(nameof(ScanSeriesAsync));

            if (!Directory.Exists(rootPath))
            {
                _logger.Warning("Wurzelverzeichnis existiert nicht: {Path}", PathRedactor.Redact(rootPath));
                return [];
            }

            ScanProgressCounter counter = new(progress, _directories.CountAudioFiles(rootPath));
            EpisodeFolderParser parser = new(folderPattern);
            List<LocalScanResult> results = [];

            foreach (string seriesFolder in _directories.GetSubdirectories(rootPath))
            {
                // Je Serie prüfen, damit der Scan beim Schließen der Anwendung zügig endet.
                ct.ThrowIfCancellationRequested();
                LocalScanResult? scanResult = ScanSeries(seriesFolder, parser, counter);

                if (scanResult is not null)
                {
                    results.Add(scanResult);
                    onSeriesScanned?.Report(scanResult);
                }
            }

            _logger.Info("Scan abgeschlossen: {SeriesCount} Serien gefunden in {Path}", results.Count, PathRedactor.Redact(rootPath));

            return results;
        }

        /// <summary>
        /// Scannt einen einzelnen Serienordner und gibt das Ergebnis zurück.
        /// Meldet nach jeder Episode den aktuellen Fortschritt.
        /// </summary>
        private LocalScanResult? ScanSeries(string seriesFolderPath, EpisodeFolderParser parser, ScanProgressCounter counter)
        {
            string seriesName = Path.GetFileName(seriesFolderPath);
            List<LocalEpisodeScan> episodes = AssignFolderNumbers(
                ScanEpisodeFolders(seriesFolderPath, seriesName, parser, counter), parser);

            // Flache Serie: Audiodateien direkt im Serienordner.
            // Greift, wenn keine Episoden aus Unterordnern gefunden wurden,
            // auch bei vorhandenen Unterordnern (z.B. Pumuckl hat einen "Kinderparty"-Ordner
            // ohne Audio, aber 86 MP3-Dateien direkt im Serienordner).
            if (episodes.Count == 0)
            {
                episodes = ScanFlatSeries(seriesFolderPath, seriesName, parser, counter);
            }

            if (episodes.Count == 0)
            {
                _logger.Debug(() => $"Keine Episodenordner erkannt in: {seriesName}");
                return null;
            }

            return new LocalScanResult
            {
                SeriesName = seriesName,
                SeriesFolderPath = seriesFolderPath,
                Episodes = episodes
            };
        }

        /// <summary>
        /// Liest die Folgenordner einer Serie. Ordner ohne Treffer des Musters werden trotzdem
        /// eingelesen; der Ordnername dient dann als Titel. Ordner ohne Audiodateien entfallen.
        /// </summary>
        private List<LocalEpisodeScan> ScanEpisodeFolders(
            string seriesFolderPath, string seriesName, EpisodeFolderParser parser, ScanProgressCounter counter)
        {
            List<LocalEpisodeScan> episodes = [];

            foreach (string episodeFolder in _directories.GetSubdirectories(seriesFolderPath))
            {
                string folderName = Path.GetFileName(episodeFolder);

                // Kein Treffer ist kein Fehler, der Ordnername dient dann als Titel.
                bool matched = parser.TryParse(folderName, out int? number, out string? parsedTitle);
                _logger.Debug(matched
                    ? $"Muster erkannt: '{folderName}' → Nummer {number}, Titel '{parsedTitle}'"
                    : $"Kein Muster-Treffer: '{folderName}' – Ordnername als Titel");

                List<string> trackPaths = _directories.CollectTrackPaths(episodeFolder);

                if (trackPaths.Count == 0)
                {
                    _logger.Debug(() => $"Episodenordner ohne Audiodateien übersprungen: {folderName}");
                    continue;
                }

                // Die Nummer vergibt AssignFolderNumbers erst, wenn alle Ordner der Serie
                // bekannt sind: Ob ein Ordner ohne Nummer eine Sonderfolge ist oder die Serie
                // gar nicht nummeriert, entscheidet sich erst im Vergleich.
                episodes.Add(new LocalEpisodeScan
                {
                    FolderPath = episodeFolder,
                    ParsedTitle = ResolveEpisodeTitle(parsedTitle ?? folderName),
                    TrackPaths = trackPaths
                });

                counter.Advance(trackPaths.Count, seriesName);
            }

            return episodes;
        }

        /// <summary>
        /// Liest eine flache Serie: jede Audiodatei direkt im Serienordner ist eine Folge.
        /// </summary>
        private List<LocalEpisodeScan> ScanFlatSeries(
            string seriesFolderPath, string seriesName, EpisodeFolderParser parser, ScanProgressCounter counter)
        {
            List<string> flatTracks = _directories.CollectTrackPaths(seriesFolderPath);
            if (flatTracks.Count == 0)
            {
                return [];
            }

            _logger.Debug(() => $"Flache Serie erkannt: {seriesName} – {flatTracks.Count} Audiodateien direkt im Ordner");

            // Mehrere Parser-Muster probieren, denn Dateinamen folgen anderen Konventionen
            // als Ordnernamen (z.B. "Karl May - 001 - Durch die Wüste")
            EpisodeFolderParser[] fileParsers = EpisodeFolderParser.CreateFileNameParserChain(parser);
            List<LocalEpisodeScan> episodes = [];

            foreach (string trackPath in flatTracks)
            {
                (int? number, string title) = FileNameEpisodeParser.Parse(Path.GetFileNameWithoutExtension(trackPath), fileParsers);

                episodes.Add(new LocalEpisodeScan
                {
                    FolderPath = seriesFolderPath,
                    ParsedNumber = number,
                    ParsedTitle = ResolveEpisodeTitle(title),
                    TrackPaths = [trackPath]
                });

                counter.Advance(1, seriesName);
            }

            return episodes;
        }

        /// <summary>
        /// Vergibt die Folgennummern aller Ordner einer Serie nach derselben Regel, die auch der
        /// Bericht „Fehlende Folgen" benutzt (<see cref="LocalEpisodeNumbers.Assign"/>). Das
        /// eingestellte Muster hat Vorrang, passt es nicht, greifen die allgemeinen Muster.
        /// </summary>
        /// <remarks>
        /// Das eingestellte Muster allein reicht nicht: „Die drei Ausrufezeichen - 082 - …" passt
        /// nicht auf <c>{number:000} - {title}</c> und bliebe ohne Nummer.
        /// </remarks>
        private static List<LocalEpisodeScan> AssignFolderNumbers(List<LocalEpisodeScan> episodes, EpisodeFolderParser parser)
        {
            if (episodes.Count == 0)
            {
                return episodes;
            }

            string[] folderNames = [.. episodes.Select(episode => Path.GetFileName(episode.FolderPath))];
            IReadOnlyList<int?> numbers = LocalEpisodeNumbers.Assign(
                folderNames,
                name => parser.TryParse(name, out int? number, out _) ? number : null);

            List<LocalEpisodeScan> numbered = new(episodes.Count);
            for (int i = 0; i < episodes.Count; i++)
            {
                numbered.Add(new LocalEpisodeScan
                {
                    FolderPath = episodes[i].FolderPath,
                    ParsedNumber = numbers[i],
                    ParsedTitle = episodes[i].ParsedTitle,
                    TrackPaths = episodes[i].TrackPaths
                });
            }

            return numbered;
        }

        /// <summary>
        /// Der Episodentitel aus dem Ordner- oder Dateinamen, ohne vorangestellte laufende Nummer.
        /// </summary>
        /// <remarks>
        /// Die ID3-Tags bleiben hier außen vor. <c>Tag.Title</c> nennt bei mehrteiligen
        /// Hörspielen das Kapitel („Inhaltsangabe", „Teil 1"), nicht die Folge; den Namen hat
        /// dagegen jemand nach seinem Schema vergeben.
        /// </remarks>
        private static string ResolveEpisodeTitle(string nameDerivedTitle)
            => EpisodeFolderParser.StripLeadingSequenceNumber(nameDerivedTitle);
    }
}
