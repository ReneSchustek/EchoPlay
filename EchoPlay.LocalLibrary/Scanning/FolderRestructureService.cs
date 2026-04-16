using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Baut eine flache Dateistruktur (alle MP3s im Serienordner) in die
    /// Standard-Ordnerstruktur um (ein Unterordner pro Folge).
    /// Verwendet dieselbe Nummernextraktion wie der Scanner, damit Kassetten-Rips
    /// und verschiedene Dateinamen-Konventionen korrekt behandelt werden.
    /// </summary>
    public sealed class FolderRestructureService : IFolderRestructureService
    {
        /// <summary>
        /// Kassetten-Muster – identisch mit dem im <see cref="LocalLibraryScanner"/>.
        /// Erkennt Dateien wie "01a Titel", "Serie - 01a - Titel", "W03a Titel", "11a".
        /// </summary>
        private static readonly Regex CassettePattern = new(
            @"^(?:.*\s-\s)?(?<prefix>[A-Za-z]*)(?<number>\d{1,3})(?<side>[abAB])(?:\s-\s|\s)(?<title>.+)$|" +
            @"^(?<prefix>[A-Za-z]*)(?<number>\d{1,3})(?<side>[abAB])$",
            RegexOptions.Compiled);

        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Service mit einer Logger-Factory.
        /// </summary>
        /// <param name="loggerFactory">Factory für den Logger.</param>
        public FolderRestructureService(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger(nameof(FolderRestructureService));
        }

        /// <inheritdoc />
        public RestructurePreview Analyze(string seriesFolderPath, string folderPattern)
        {
            if (!Directory.Exists(seriesFolderPath))
            {
                return new RestructurePreview
                {
                    SeriesFolderPath = seriesFolderPath,
                    Actions = [],
                    FolderCount = 0
                };
            }

            // Nur Audiodateien direkt im Serienordner sammeln (keine Unterordner)
            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(seriesFolderPath);
            }
            catch (IOException)
            {
                return EmptyPreview(seriesFolderPath);
            }
            catch (UnauthorizedAccessException)
            {
                return EmptyPreview(seriesFolderPath);
            }

            List<string> audioFiles = [];
            foreach (string file in allFiles)
            {
                if (EchoPlay.Core.AudioExtensions.IsAudioFile(file))
                {
                    audioFiles.Add(file);
                }
            }

            if (audioFiles.Count == 0)
            {
                return EmptyPreview(seriesFolderPath);
            }

            // Parser für die Nummern/Titel-Extraktion – gleiche Muster wie im Scanner
            EpisodeFolderParser configuredParser = new(folderPattern);
            EpisodeFolderParser[] fileParsers =
            [
                configuredParser,
                new("{*} - {number:000} - {title}"),
                new("{number:000} - {title}"),
                new("{number} {title}"),
                new("{*}_FOLGE_{number}_{title}"),
            ];

            List<RestructureAction> actions = [];
            HashSet<string> targetFolders = [];

            foreach (string filePath in audioFiles)
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string fileName = Path.GetFileName(filePath);
                int? episodeNumber = null;
                string? title = null;

                // Kassetten-Rip prüfen
                Match cassetteMatch = CassettePattern.Match(fileNameWithoutExt);
                if (cassetteMatch.Success)
                {
                    string cassetteNumberStr = cassetteMatch.Groups["number"].Value;
                    char side = char.ToLowerInvariant(cassetteMatch.Groups["side"].Value[0]);

                    if (int.TryParse(cassetteNumberStr, out int cassetteNumber))
                    {
                        episodeNumber = (cassetteNumber * 2) - (side == 'a' ? 1 : 0);
                        title = cassetteMatch.Groups["title"].Success && cassetteMatch.Groups["title"].Value.Length > 0
                            ? cassetteMatch.Groups["title"].Value.Trim()
                            : fileNameWithoutExt;
                    }
                }

                // Generische Parser
                if (episodeNumber is null)
                {
                    foreach (EpisodeFolderParser parser in fileParsers)
                    {
                        if (parser.TryParse(fileNameWithoutExt, out episodeNumber, out title))
                        {
                            break;
                        }
                    }
                }

                // Kein Muster erkannt – Dateiname als Titel, keine Nummer
                if (title is null)
                {
                    title = fileNameWithoutExt;
                }

                // Zielordnernamen generieren
                string targetFolderName = episodeNumber.HasValue
                    ? $"{episodeNumber.Value:D3} - {SanitizeFolderName(title)}"
                    : SanitizeFolderName(title);

                string targetFolderPath = Path.Combine(seriesFolderPath, targetFolderName);
                _ = targetFolders.Add(targetFolderName);

                actions.Add(new RestructureAction
                {
                    SourcePath = filePath,
                    TargetFolderPath = targetFolderPath,
                    TargetFolderName = targetFolderName,
                    FileName = fileName,
                    EpisodeNumber = episodeNumber,
                    EpisodeTitle = title
                });
            }

            // Nach Episodennummer sortieren – Dateien ohne Nummer ans Ende
            actions.Sort((a, b) =>
            {
                int numA = a.EpisodeNumber ?? int.MaxValue;
                int numB = b.EpisodeNumber ?? int.MaxValue;
                return numA.CompareTo(numB);
            });

            _logger.Info($"Ordnerstruktur-Analyse: {actions.Count} Dateien → {targetFolders.Count} Ordner in '{Path.GetFileName(seriesFolderPath)}'");

            return new RestructurePreview
            {
                SeriesFolderPath = seriesFolderPath,
                Actions = actions,
                FolderCount = targetFolders.Count
            };
        }

        /// <summary>Dateiname des Journals im Serienordner – wird bei erfolgreichem Umbau gelöscht.</summary>
        public const string JournalFileName = ".echoplay-restructure-journal.json";

        /// <inheritdoc />
        public int Execute(RestructurePreview preview)
        {
            ArgumentNullException.ThrowIfNull(preview);

            string journalPath = Path.Combine(preview.SeriesFolderPath, JournalFileName);
            List<(string Source, string Destination)> completed = [];

            // Leeres Journal vor dem ersten Move persistieren, damit ein Crash
            // zwischen File.Move und dem Journal-Update trotzdem wiederherstellbar ist.
            TryWriteJournal(journalPath, completed);

            try
            {
                foreach (RestructureAction action in preview.Actions)
                {
                    // Zielordner anlegen (falls noch nicht vorhanden)
                    _ = Directory.CreateDirectory(action.TargetFolderPath);

                    string destination = Path.Combine(action.TargetFolderPath, action.FileName);

                    // Prüfen ob die Zieldatei bereits existiert – Kollision vermeiden
                    if (File.Exists(destination))
                    {
                        _logger.Warning($"Zieldatei existiert bereits, übersprungen: {destination}");
                        continue;
                    }

                    File.Move(action.SourcePath, destination);
                    completed.Add((action.SourcePath, destination));
                    TryWriteJournal(journalPath, completed);
                }

                _logger.Info($"Ordnerstruktur aufgebaut: {completed.Count} Dateien verschoben in '{Path.GetFileName(preview.SeriesFolderPath)}'");
                TryDeleteJournal(journalPath);
                return completed.Count;
            }
            catch (Exception ex)
            {
                // Rollback: bereits verschobene Dateien zurückschieben
                _logger.Error($"Fehler beim Verschieben, starte Rollback: {ex.Message}", ex);

                foreach ((string source, string destination) in completed)
                {
                    try
                    {
                        File.Move(destination, source);
                    }
                    catch (Exception rollbackEx) when (rollbackEx is IOException
                                                       or UnauthorizedAccessException
                                                       or SecurityException
                                                       or PathTooLongException
                                                       or DirectoryNotFoundException
                                                       or NotSupportedException
                                                       or ArgumentException)
                    {
                        _logger.Error($"Rollback fehlgeschlagen für '{source}': {rollbackEx.Message}", rollbackEx);
                    }
                }

                // Leere Ordner aufräumen, die durch den Rollback entstanden sind
                CleanupEmptyFolders(preview);
                TryDeleteJournal(journalPath);

                throw;
            }
        }

        /// <summary>
        /// Versucht, ein liegengebliebenes Journal im Serienordner wiederzugeben – etwa nach einem
        /// Prozess-Crash zwischen zwei <c>File.Move</c>-Operationen. Gibt die Zahl der zurückgerollten
        /// Dateien zurück (0 wenn kein Journal existiert oder keine Einträge zu rollen sind).
        /// </summary>
        /// <param name="seriesFolderPath">Serienordner, der geprüft werden soll.</param>
        /// <returns>Anzahl wiederhergestellter Dateien.</returns>
        public int TryRecoverPendingJournal(string seriesFolderPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(seriesFolderPath);

            string journalPath = Path.Combine(seriesFolderPath, JournalFileName);
            if (!File.Exists(journalPath))
            {
                return 0;
            }

            List<JournalEntry>? entries;
            try
            {
                string json = File.ReadAllText(journalPath);
                entries = JsonSerializer.Deserialize<List<JournalEntry>>(json);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.Warning($"Journal '{journalPath}' konnte nicht gelesen werden: {ex.Message}");
                return 0;
            }

            if (entries is null || entries.Count == 0)
            {
                TryDeleteJournal(journalPath);
                return 0;
            }

            int recovered = 0;
            foreach (JournalEntry entry in entries)
            {
                try
                {
                    if (File.Exists(entry.Destination) && !File.Exists(entry.Source))
                    {
                        File.Move(entry.Destination, entry.Source);
                        recovered++;
                    }
                }
                catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or SecurityException
                                           or PathTooLongException
                                           or DirectoryNotFoundException
                                           or NotSupportedException
                                           or ArgumentException)
                {
                    _logger.Error($"Journal-Recovery fehlgeschlagen für '{entry.Destination}': {ex.Message}", ex);
                }
            }

            _logger.Info($"Journal-Recovery: {recovered} von {entries.Count} Dateien zurückgerollt in '{Path.GetFileName(seriesFolderPath)}'");
            TryDeleteJournal(journalPath);
            return recovered;
        }

        private void TryWriteJournal(string journalPath, List<(string Source, string Destination)> completed)
        {
            try
            {
                List<JournalEntry> entries = [];
                foreach ((string source, string destination) in completed)
                {
                    entries.Add(new JournalEntry(source, destination));
                }

                string json = JsonSerializer.Serialize(entries);
                File.WriteAllText(journalPath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Journal ist Best-Effort – der Umbau läuft auch ohne persistentes Journal weiter.
                _logger.Warning($"Journal '{journalPath}' konnte nicht geschrieben werden: {ex.Message}");
            }
        }

        private void TryDeleteJournal(string journalPath)
        {
            try
            {
                if (File.Exists(journalPath))
                {
                    File.Delete(journalPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Journal '{journalPath}' konnte nicht gelöscht werden: {ex.Message}");
            }
        }

        private sealed record JournalEntry(string Source, string Destination);

        /// <summary>
        /// Entfernt leere Unterordner, die durch einen abgebrochenen Umbau oder Rollback entstanden sind.
        /// </summary>
        private static void CleanupEmptyFolders(RestructurePreview preview)
        {
            HashSet<string> createdFolders = [];
            foreach (RestructureAction action in preview.Actions)
            {
                _ = createdFolders.Add(action.TargetFolderPath);
            }

            foreach (string folder in createdFolders)
            {
                try
                {
                    if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                    {
                        Directory.Delete(folder);
                    }
                }
                catch (IOException)
                {
                    // Aufräumen ist Best-Effort – Fehler hier sind nicht kritisch
                }
                catch (UnauthorizedAccessException)
                {
                    // Keine Rechte zum Löschen – nicht kritisch
                }
            }
        }

        /// <summary>
        /// Entfernt ungültige Zeichen aus einem Ordnernamen.
        /// Windows erlaubt keine Zeichen wie <c>\ / : * ? " &lt; &gt; |</c> in Ordnernamen.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = name;

            foreach (char c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            return sanitized.Trim();
        }

        /// <summary>Erzeugt eine leere Vorschau für den angegebenen Pfad.</summary>
        private static RestructurePreview EmptyPreview(string seriesFolderPath)
        {
            return new RestructurePreview
            {
                SeriesFolderPath = seriesFolderPath,
                Actions = [],
                FolderCount = 0
            };
        }
    }
}
