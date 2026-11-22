using EchoPlay.Core.Logging;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using System.Text.Json;

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
        /// <summary>Dateiname des Journals im Serienordner; wird bei erfolgreichem Umbau gelöscht.</summary>
        public const string JournalFileName = ".echoplay-restructure-journal.json";

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
            List<string>? audioFiles = AudioFilesInSeriesFolder(seriesFolderPath);
            if (audioFiles is not { Count: > 0 })
            {
                return EmptyPreview(seriesFolderPath);
            }

            // Dieselbe Parser-Kette wie beim Einlesen, damit eine Datei hier dieselbe Nummer
            // bekommt, unter der sie später als Folge erscheint.
            EpisodeFolderParser[] fileParsers = EpisodeFolderParser.CreateFileNameParserChain(new EpisodeFolderParser(folderPattern));
            List<RestructureAction> actions = [.. audioFiles.Select(file => PlanMove(seriesFolderPath, file, fileParsers))];
            int folderCount = actions.Select(action => action.TargetFolderName).Distinct().Count();

            // Dateien ohne Nummer ans Ende. OrderBy ist stabil, Dateien mit gleicher Nummer
            // behalten die Reihenfolge des Ordners.
            List<RestructureAction> sorted = [.. actions.OrderBy(action => action.EpisodeNumber ?? int.MaxValue)];

            _logger.Info(
                "Ordnerstruktur-Analyse: {FileCount} Dateien → {FolderCount} Ordner in '{SeriesFolder}'",
                actions.Count, folderCount, Path.GetFileName(seriesFolderPath));

            return new RestructurePreview
            {
                SeriesFolderPath = seriesFolderPath,
                Actions = sorted,
                FolderCount = folderCount
            };
        }

        /// <summary>
        /// Die Audiodateien direkt im Serienordner; was in Unterordnern liegt, hat seine
        /// Ordnerstruktur schon. Leer, wenn der Ordner fehlt oder nicht lesbar ist.
        /// </summary>
        private List<string>? AudioFilesInSeriesFolder(string seriesFolderPath)
        {
            if (!Directory.Exists(seriesFolderPath))
            {
                return null;
            }

            try
            {
                return [.. Directory.GetFiles(seriesFolderPath).Where(EchoPlay.Core.AudioExtensions.IsAudioFile)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Serienordner für den Umbau nicht lesbar: {Folder} – {Reason}", PathRedactor.Redact(seriesFolderPath), ex.Message);
                return null;
            }
        }

        /// <summary>Wohin eine Datei wandert: in einen Ordner aus Nummer und Titel.</summary>
        private static RestructureAction PlanMove(string seriesFolderPath, string filePath, EpisodeFolderParser[] fileParsers)
        {
            (int? episodeNumber, string title) = FileNameEpisodeParser.Parse(Path.GetFileNameWithoutExtension(filePath), fileParsers);
            string targetFolderName = episodeNumber.HasValue
                ? $"{episodeNumber.Value:D3} - {EchoPlay.Core.PathSanitizer.SanitizeSegment(title)}"
                : EchoPlay.Core.PathSanitizer.SanitizeSegment(title);

            return new RestructureAction
            {
                SourcePath = filePath,
                TargetFolderPath = Path.Combine(seriesFolderPath, targetFolderName),
                TargetFolderName = targetFolderName,
                FileName = Path.GetFileName(filePath),
                EpisodeNumber = episodeNumber,
                EpisodeTitle = title
            };
        }

        /// <inheritdoc />
        public int Execute(RestructurePreview preview)
        {
            ArgumentNullException.ThrowIfNull(preview);

            string journalPath = Path.Combine(preview.SeriesFolderPath, JournalFileName);
            List<(string Source, string Destination)> planned = [];
            List<(string Source, string Destination)> completed = [];

            try
            {
                foreach (RestructureAction action in preview.Actions)
                {
                    MoveWithJournal(action, journalPath, planned, completed);
                }

                _logger.Info(
                    "Ordnerstruktur aufgebaut: {FileCount} Dateien verschoben in '{SeriesFolder}'",
                    completed.Count, Path.GetFileName(preview.SeriesFolderPath));
                TryDeleteJournal(journalPath);
                return completed.Count;
            }
            catch (Exception ex)
            {
                // Jeder Fehler nimmt den ganzen Umbau zurück und geht danach weiter nach oben;
                // eine halb umgebaute Serie wäre schwerer zu durchschauen als die alte.
                _logger.Error("Fehler beim Verschieben, starte Rollback: {Reason}", ex, ex.Message);

                RollBack(completed);
                CleanupEmptyFolders(preview);
                TryDeleteJournal(journalPath);

                throw;
            }
        }

        /// <summary>
        /// Verschiebt eine Datei in ihren Zielordner. Erst die Absicht ins Journal, dann
        /// verschieben: Endet der Prozess zwischen beidem, steht der Schritt im Journal; die
        /// Wiederherstellung schiebt nur zurück, was tatsächlich am Ziel liegt. Eine vorhandene
        /// Datei gleichen Namens wird nicht überschrieben.
        /// </summary>
        private void MoveWithJournal(
            RestructureAction action,
            string journalPath,
            List<(string Source, string Destination)> planned,
            List<(string Source, string Destination)> completed)
        {
            _ = Directory.CreateDirectory(action.TargetFolderPath);
            string destination = Path.Combine(action.TargetFolderPath, action.FileName);
            if (File.Exists(destination))
            {
                _logger.Warning("Zieldatei existiert bereits, übersprungen: {Destination}", PathRedactor.Redact(destination));
                return;
            }

            planned.Add((action.SourcePath, destination));
            TryWriteJournal(journalPath, planned);
            File.Move(action.SourcePath, destination);
            completed.Add((action.SourcePath, destination));
        }

        /// <summary>Schiebt die schon verschobenen Dateien zurück; was nicht zurückgeht, steht im Protokoll.</summary>
        private void RollBack(List<(string Source, string Destination)> completed)
        {
            foreach ((string source, string destination) in completed)
            {
                try
                {
                    File.Move(destination, source);
                }
                catch (Exception rollbackEx) when (FolderRenameJournal.IsFileSystemError(rollbackEx))
                {
                    _logger.Error("Rollback fehlgeschlagen für '{Source}': {Reason}", rollbackEx, PathRedactor.Redact(source), rollbackEx.Message);
                }
            }
        }

        /// <inheritdoc />
        public int TryRecoverPendingJournal(string seriesFolderPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(seriesFolderPath);

            string journalPath = Path.Combine(seriesFolderPath, JournalFileName);
            if (!File.Exists(journalPath))
            {
                return 0;
            }

            List<JournalEntry>? entries = ReadJournal(journalPath);
            if (entries is null)
            {
                return 0;
            }

            if (entries.Count == 0)
            {
                TryDeleteJournal(journalPath);
                return 0;
            }

            int recovered = 0;
            foreach (JournalEntry entry in entries)
            {
                recovered += TryMoveBack(entry) ? 1 : 0;
            }

            _logger.Info(
                "Journal-Recovery: {Recovered} von {Total} Dateien zurückgerollt in '{SeriesFolder}'",
                recovered, entries.Count, Path.GetFileName(seriesFolderPath));
            TryDeleteJournal(journalPath);
            return recovered;
        }

        /// <summary>Liest das Journal; ein unlesbares gilt als nicht vorhanden.</summary>
        /// <returns>Die Einträge, oder leer, wenn das Journal nicht lesbar war.</returns>
        private List<JournalEntry>? ReadJournal(string journalPath)
        {
            try
            {
                return JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(journalPath)) ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.Warning("Journal '{JournalPath}' konnte nicht gelesen werden: {Reason}", PathRedactor.Redact(journalPath), ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Schiebt eine Datei zurück, wenn sie am Ziel liegt und die Quelle frei ist.
        /// </summary>
        /// <returns><see langword="true"/>, wenn die Datei zurückgeschoben wurde.</returns>
        private bool TryMoveBack(JournalEntry entry)
        {
            try
            {
                if (File.Exists(entry.Destination) && !File.Exists(entry.Source))
                {
                    File.Move(entry.Destination, entry.Source);
                    return true;
                }
            }
            catch (Exception ex) when (FolderRenameJournal.IsFileSystemError(ex))
            {
                _logger.Error("Journal-Recovery fehlgeschlagen für '{Destination}': {Reason}", ex, PathRedactor.Redact(entry.Destination), ex.Message);
            }

            return false;
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
            catch (Exception ex) when (FolderRenameJournal.IsFileSystemError(ex))
            {
                // Ohne Journal läuft der Umbau weiter; nur ein Absturz mittendrin ließe sich dann
                // nicht mehr zurückspielen. Ein Abbruch wegen des Journals wäre der größere Schaden.
                _logger.Warning("Journal '{JournalPath}' konnte nicht geschrieben werden: {Reason}", PathRedactor.Redact(journalPath), ex.Message);
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
                _logger.Warning("Journal '{JournalPath}' konnte nicht gelöscht werden: {Reason}", PathRedactor.Redact(journalPath), ex.Message);
            }
        }

        /// <summary>
        /// Ein Verschiebeschritt im Journal des Serienordners, festgehalten vor der Ausführung.
        /// <see cref="TryRecoverPendingJournal"/> liest diese Einträge und schiebt die Dateien
        /// zurück, wenn ein Umbau mittendrin abbricht.
        /// </summary>
        private sealed record JournalEntry(string Source, string Destination);

        /// <summary>
        /// Entfernt leere Unterordner, die durch einen abgebrochenen Umbau oder Rollback entstanden sind.
        /// </summary>
        private void CleanupEmptyFolders(RestructurePreview preview)
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ein leerer Ordner, der stehen bleibt, stört nicht; die Dateien sind zurück.
                    _logger.Warning("Leerer Ordner nach dem Rollback nicht gelöscht: {Folder} – {Reason}", PathRedactor.Redact(folder), ex.Message);
                }
            }
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
