using EchoPlay.Core.Logging;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using System.Security;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Durchsucht eine lokale Verzeichnisstruktur nach Hörspielserien und Episoden.
    /// Unterstützt drei Hierarchieebenen: Root → Serienordner → Episodenordner → Audiodateien.
    /// Alternativ auch flache Struktur: Root → Serienordner → direkte Audiodateien.
    /// IO-Fehler auf Ordnerebene werden geloggt und übersprungen, kein Absturz.
    /// </summary>
    public sealed class LocalLibraryScanner : ILocalLibraryScanner
    {
        private readonly ILogger _logger;
        private readonly ITagTitleReader _tagTitleReader;

        /// <summary>
        /// Initialisiert den Scanner mit Logger-Fabrik und Tag-Leser.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="tagTitleReader">Liest ID3-Tags aus Audiodateien für bessere Titelqualität.</param>
        public LocalLibraryScanner(ILoggerFactory loggerFactory, ITagTitleReader tagTitleReader)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger(nameof(LocalLibraryScanner));
            _tagTitleReader = tagTitleReader;
        }

        /// <summary>
        /// Gibt alle direkten Unterordner des Wurzelverzeichnisses zurück – ohne Episodenscan.
        /// Extrem schnell: nur ein <c>Directory.GetDirectories</c>-Aufruf, kein IO auf Dateiebene.
        /// Gibt eine leere Liste zurück wenn das Verzeichnis nicht existiert oder ein IO-Fehler auftritt.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis der Bibliothek.</param>
        /// <returns>Sortierte Liste der Unterordnerpfade.</returns>
        public IReadOnlyList<string> GetSeriesFolders(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return [];
            }

            return GetSubdirectories(rootPath);
        }

        /// <summary>
        /// Durchsucht das angegebene Wurzelverzeichnis nach Hörspielserien und Episoden.
        /// Läuft auf einem Hintergrundthread (Task.Run), damit der UI-Thread nicht blockiert wird.
        /// Ermittelt vor der Scan-Schleife die Gesamtanzahl aller Audiodateien, um einen
        /// deterministischen Fortschrittsbalken zu ermöglichen.
        /// Episodenordner werden unabhängig vom konfigurierten Muster importiert –
        /// stimmt der Ordnername nicht mit dem Muster überein, wird der Ordnername als Titel verwendet.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis der Bibliothek.</param>
        /// <param name="folderPattern">
        /// Muster für Episodenordner, z.B. <c>"{number:000} - {title}"</c>.
        /// Unterstützte Platzhalter: <c>{number}</c>, <c>{number:000}</c>, <c>{title}</c>.
        /// Passt der Ordnername nicht, wird er trotzdem importiert – nur <see cref="LocalEpisodeScan.ParsedNumber"/>
        /// bleibt dann <see langword="null"/>.
        /// </param>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback – erhält <see cref="ScanProgress"/> nach jeder
        /// verarbeiteten Episode. Null wenn kein Fortschritt gemeldet werden soll.
        /// </param>
        /// <param name="onSeriesScanned">
        /// Optionaler Callback, der nach jeder vollständig gescannten Serie aufgerufen wird.
        /// Ermöglicht es dem Aufrufer, Serien sofort zu verarbeiten, ohne auf das Gesamtergebnis
        /// zu warten – Grundlage für die progressive Anzeige in der UI.
        /// </param>
        /// <param name="ct">Abbruchtoken – ermöglicht sauberes Beenden bei App-Schließen.</param>
        /// <returns>
        /// Liste aller gefundenen Serien mit ihren Episoden.
        /// Gibt eine leere Liste zurück, wenn das Verzeichnis nicht existiert oder leer ist.
        /// </returns>
        public Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress = null,
            IProgress<LocalScanResult>? onSeriesScanned = null,
            CancellationToken ct = default)
        {
            // Task.Run lagert den synchronen Dateisystem-Zugriff auf den Threadpool aus –
            // der UI-Thread bleibt während des Scans vollständig reaktionsfähig.
            // Progress<T> leitet die Callbacks sicher auf den UI-Thread zurück.
            // CancellationToken wird an Task.Run übergeben, damit der Task vor dem Start
            // abbrechbar ist, und an ExecuteScan, damit auch die Schleife darin abbricht.
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
            CancellationToken ct = default)
        {
            using LogScope scope = _logger.BeginScope(nameof(ScanSeriesAsync));

            if (!Directory.Exists(rootPath))
            {
                _logger.Warning("Wurzelverzeichnis existiert nicht: {Path}", PathRedactor.Redact(rootPath));
                return [];
            }

            // Gesamtzahl aller Audiodateien vor der Scan-Schleife ermitteln –
            // nur so ist ein deterministischer Fortschrittsbalken möglich.
            int totalFiles = CountSupportedFiles(rootPath);
            int processedFiles = 0;

            EpisodeFolderParser parser = new(folderPattern);
            List<LocalScanResult> results = [];

            string[] seriesFolders = GetSubdirectories(rootPath);

            foreach (string seriesFolder in seriesFolders)
            {
                // Abbruchprüfung pro Serie – ermöglicht sauberes Beenden bei App-Schließen
                ct.ThrowIfCancellationRequested();
                LocalScanResult? scanResult = ScanSeries(
                    seriesFolder,
                    parser,
                    progress,
                    totalFiles,
                    ref processedFiles);

                if (scanResult is not null)
                {
                    results.Add(scanResult);
                    // Sofort melden, damit der Aufrufer die Serie ohne Wartezeit verarbeiten kann.
                    // Progress<T> überträgt den Aufruf sicher auf den SynchronizationContext des Aufrufers.
                    onSeriesScanned?.Report(scanResult);
                }
            }

            _logger.Info("Scan abgeschlossen: {SeriesCount} Serien gefunden in {Path}", results.Count, PathRedactor.Redact(rootPath));

            return results;
        }

        /// <summary>
        /// Scannt einen einzelnen Serienordner und gibt das Ergebnis zurück.
        /// Meldet nach jeder Episode den aktuellen Fortschritt.
        /// Episodenordner ohne Mustert-Treffer werden trotzdem importiert –
        /// der Ordnername wird dann als Episodentitel verwendet.
        /// </summary>
        private LocalScanResult? ScanSeries(
            string seriesFolderPath,
            EpisodeFolderParser parser,
            IProgress<ScanProgress>? progress,
            int totalFiles,
            ref int processedFiles)
        {
            string seriesName = Path.GetFileName(seriesFolderPath);
            string[] episodeFolders = GetSubdirectories(seriesFolderPath);
            List<LocalEpisodeScan> episodes = [];

            foreach (string episodeFolder in episodeFolders)
            {
                string folderName = Path.GetFileName(episodeFolder);

                // Versuche, Nummer und Titel aus dem Ordnernamen zu parsen.
                // Kein Treffer ist kein Fehler – der Ordnername dient dann als Titel.
                bool matched = parser.TryParse(folderName, out int? number, out string? parsedTitle);
                _logger.Debug(matched
                    ? $"Muster erkannt: '{folderName}' → Nummer {number}, Titel '{parsedTitle}'"
                    : $"Kein Muster-Treffer: '{folderName}' – Ordnername als Titel");

                List<string> trackPaths = CollectTrackPaths(episodeFolder);

                // Ordner ohne Audiodateien überspringen – leer bedeutet keine Folge
                if (trackPaths.Count == 0)
                {
                    _logger.Debug(() => $"Episodenordner ohne Audiodateien übersprungen: {folderName}");
                    continue;
                }

                // Verwende den geparsten Titel oder den Ordnernamen als Fallback
                string titleSource = parsedTitle ?? folderName;
                string? episodeTitle = ResolveEpisodeTitle(trackPaths, titleSource);

                episodes.Add(new LocalEpisodeScan
                {
                    FolderPath = episodeFolder,
                    ParsedNumber = number,
                    ParsedTitle = episodeTitle,
                    TrackPaths = trackPaths
                });

                // Fortschritt nach jeder Episode melden – Tracks dieser Episode zählen hoch
                processedFiles += trackPaths.Count;
                progress?.Report(new ScanProgress
                {
                    ProcessedFiles = processedFiles,
                    TotalFiles = totalFiles,
                    StatusText = $"Scanne {seriesName} …"
                });
            }

            // Flache Serie: Audiodateien direkt im Serienordner.
            // Greift wenn keine Episoden aus Unterordnern gefunden wurden –
            // auch bei vorhandenen Unterordnern (z.B. Pumuckl hat einen "Kinderparty"-Ordner
            // ohne Audio, aber 86 MP3-Dateien direkt im Serienordner).
            if (episodes.Count == 0)
            {
                List<string> flatTracks = CollectTrackPaths(seriesFolderPath);

                if (flatTracks.Count > 0)
                {
                    _logger.Debug(() => $"Flache Serie erkannt: {seriesName} – {flatTracks.Count} Audiodateien direkt im Ordner");

                    // Mehrere Parser-Muster probieren – Dateinamen folgen anderen Konventionen
                    // als Ordnernamen (z.B. "Karl May - 001 - Durch die Wüste")
                    EpisodeFolderParser[] fileParsers = EpisodeFolderParser.CreateFileNameParserChain(parser);

                    foreach (string trackPath in flatTracks)
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(trackPath);

                        // Parse-Kaskade (Kassette → Parser-Kette → Fallback) zentral in FileNameEpisodeParser.
                        (int? number, string title) = FileNameEpisodeParser.Parse(fileNameWithoutExt, fileParsers);

                        string? episodeTitle = ResolveEpisodeTitle([trackPath], title);

                        episodes.Add(new LocalEpisodeScan
                        {
                            FolderPath = seriesFolderPath,
                            ParsedNumber = number,
                            ParsedTitle = episodeTitle,
                            TrackPaths = [trackPath]
                        });

                        processedFiles++;
                        progress?.Report(new ScanProgress
                        {
                            ProcessedFiles = processedFiles,
                            TotalFiles = totalFiles,
                            StatusText = $"Scanne {seriesName} …"
                        });
                    }
                }
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
        /// Ermittelt den Episodentitel mit der bestmöglichen Qualität.
        /// Priorität: bereinigter Ordnername > ID3-Album-Tag > roher Ordnername.
        /// <para>
        /// <c>Tag.Title</c> wird bewusst ignoriert: Bei mehrteiligen Hörspielen enthält er
        /// den Kapitel- oder Tracktitel (z.B. „Inhaltsangabe", „Teil 1", „09.04.2021"),
        /// nicht den Episodentitel. Der Ordnername ist die verlässlichste Quelle.
        /// </para>
        /// </summary>
        /// <param name="trackPaths">Audiodateien der Episode – der erste Track wird für Album-Tag genutzt.</param>
        /// <param name="folderDerivedTitle">Aus dem Ordnernamen ermittelter Titel (kann Laufnummern enthalten).</param>
        /// <returns>Bester verfügbarer Episodentitel oder null, wenn keiner ermittelt werden konnte.</returns>
        private string? ResolveEpisodeTitle(List<string> trackPaths, string? folderDerivedTitle)
        {
            // Ordnername hat Vorrang – der Nutzer hat die Episoden nach diesem Schema abgelegt
            if (folderDerivedTitle is not null)
            {
                return EpisodeFolderParser.StripLeadingSequenceNumber(folderDerivedTitle);
            }

            // Selten: kein Ordnername verfügbar (z.B. flache Serie direkt im Serienordner).
            // Tag.Album enthält bei korrekt getaggten Hörspielen den Episoden- oder Serientitel.
            if (trackPaths.Count > 0)
            {
                string? albumTitle = TryReadAlbumTitle(trackPaths[0]);

                if (albumTitle is not null)
                {
                    return albumTitle;
                }
            }

            return null;
        }

        /// <summary>
        /// Liest den ID3-Album-Tag der angegebenen Audiodatei.
        /// <c>Tag.Title</c> wird nicht verwendet – er enthält den Kapitel-Titel, nicht den Episodentitel.
        /// Fehlerhafte oder nicht unterstützte Dateien geben null zurück.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>Album-Tag oder null, wenn nicht lesbar oder leer.</returns>
        private string? TryReadAlbumTitle(string filePath)
        {
            try
            {
                (string _, string album) = _tagTitleReader.Read(filePath);

                return string.IsNullOrWhiteSpace(album) ? null : album;
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or PathTooLongException
                                       or DirectoryNotFoundException
                                       or NotSupportedException
                                       or TagLib.CorruptFileException
                                       or TagLib.UnsupportedFormatException)
            {
                _logger.Warning("ID3-Tags nicht lesbar, Ordnername wird verwendet: {Path} – {Reason}", PathRedactor.Redact(filePath), ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Zählt alle unterstützten Audiodateien im angegebenen Verzeichnis rekursiv.
        /// Gibt 0 zurück bei IO-Fehlern – der Aufrufer soll dann indeterministischen Fortschritt zeigen.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis.</param>
        /// <returns>Anzahl der Audiodateien oder 0 bei Fehler.</returns>
        private int CountSupportedFiles(string rootPath)
        {
            try
            {
                return Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                    .Count(EchoPlay.Core.AudioExtensions.IsAudioFile);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or PathTooLongException
                                       or DirectoryNotFoundException
                                       or ArgumentException)
            {
                _logger.Warning("Vorab-Zählung fehlgeschlagen, Fortschritt wird indeterministisch: {Reason}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Sammelt alle unterstützten Audiodateien im angegebenen Ordner, sortiert nach Dateiname.
        /// Nur direkte Dateien des Ordners werden berücksichtigt, keine Unterordner.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum Episodenordner.</param>
        /// <returns>Sortierte Liste der absoluten Dateipfade.</returns>
        private List<string> CollectTrackPaths(string folderPath)
        {
            try
            {
                string[] files = Directory.GetFiles(folderPath);

                List<string> tracks = files
                    .Where(EchoPlay.Core.AudioExtensions.IsAudioFile)
                    .Order()
                    .ToList();

                return tracks;
            }
            catch (IOException ex)
            {
                _logger.Warning("Audiodateien konnten nicht gelesen werden in: {Path} – {Reason}", PathRedactor.Redact(folderPath), ex.Message);
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning("Zugriff verweigert für: {Path} – {Reason}", PathRedactor.Redact(folderPath), ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Liest alle direkten Unterordner des angegebenen Verzeichnisses.
        /// Bei IO-Fehlern wird ein leeres Array zurückgegeben und der Fehler geloggt.
        /// </summary>
        /// <param name="path">Absoluter Pfad zum Verzeichnis.</param>
        /// <returns>Array der Unterordnerpfade oder leeres Array bei Fehler.</returns>
        private string[] GetSubdirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (IOException ex)
            {
                _logger.Warning("Unterordner konnten nicht gelesen werden: {Path} – {Reason}", PathRedactor.Redact(path), ex.Message);
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning("Zugriff verweigert für: {Path} – {Reason}", PathRedactor.Redact(path), ex.Message);
                return [];
            }
        }
    }
}
