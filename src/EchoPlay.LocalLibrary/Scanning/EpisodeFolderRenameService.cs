using EchoPlay.Core.Logging;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Benennt die Folgenordner einer Serie nach dem eingestellten Muster um. Die Nummer kommt
    /// aus dem Ordnernamen nach derselben Regel wie beim Einlesen, der Titel aus dem Album-Tag;
    /// fehlt der Tag, aus dem Ordnernamen.
    /// </summary>
    /// <remarks>
    /// Jeder Schritt steht im <see cref="FolderRenameJournal"/>, bevor er ausgeführt wird. Bricht
    /// der Prozess mittendrin ab, spielt die nächste Vorschau das Journal zurück, und kein Ordner
    /// bleibt halb umbenannt zurück.
    /// </remarks>
    public sealed class EpisodeFolderRenameService : IEpisodeFolderRenameService
    {
        /// <summary>Dateiname des Journals im Serienordner; nach einem vollständigen Lauf gelöscht.</summary>
        public const string JournalFileName = FolderRenameJournal.FileName;

        // Windows lässt ohne Langpfad-Schalter für Verzeichnisse 248 Zeichen samt abschließendem
        // Nullzeichen zu; die übrigen zwölf bis MAX_PATH bleiben für einen Dateinamen darin.
        private const int MaxDirectoryPathLength = 247;

        private readonly ILogger _logger;
        private readonly ITagTitleReader _tagTitleReader;

        /// <summary>
        /// Richtet den Dienst ein.
        /// </summary>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
        /// <param name="tagTitleReader">Liest den Album-Tag einer Audiodatei.</param>
        public EpisodeFolderRenameService(ILoggerFactory loggerFactory, ITagTitleReader tagTitleReader)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(tagTitleReader);

            _logger = loggerFactory.CreateLogger(nameof(EpisodeFolderRenameService));
            _tagTitleReader = tagTitleReader;
        }

        /// <inheritdoc />
        public FolderRenamePlan Plan(string seriesFolderPath, string folderPattern)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(seriesFolderPath);
            ArgumentNullException.ThrowIfNull(folderPattern);

            int recovered = new FolderRenameJournal(seriesFolderPath, _logger).Recover(Move);
            string[] episodeFolders = [.. Directory.GetDirectories(seriesFolderPath).Where(EpisodeAudioFiles.HasAudio)];
            string[] folderNames = [.. episodeFolders.Select(folder => Path.GetFileName(folder))];
            EpisodeFolderParser parser = new(folderPattern);
            int? Preferred(string name) => parser.TryParse(name, out int? number, out _) ? number : null;

            bool patternSupported = EpisodeFolderNaming.Render(folderPattern, 1, "x") is not null;
            bool seriesIsNumbered = LocalEpisodeNumbers.IsNumbered(folderNames, Preferred);
            IReadOnlyList<FolderRenameItem> items = patternSupported && seriesIsNumbered
                ? PlanFolders(episodeFolders, LocalEpisodeNumbers.Assign(folderNames, Preferred), folderPattern, parser)
                : [.. episodeFolders.Select(Unchanged)];

            return new FolderRenamePlan
            {
                SeriesFolderPath = seriesFolderPath,
                Items = items,
                PatternSupported = patternSupported,
                SeriesIsNumbered = seriesIsNumbered,
                RecoveredCount = recovered
            };
        }

        /// <inheritdoc />
        public FolderRenameResult Execute(FolderRenamePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            FolderRenameJournal journal = new(plan.SeriesFolderPath, _logger);
            List<(string OldPath, string NewPath)> moves = [];
            int skipped;
            try
            {
                skipped = RenameAll(plan.Renames, journal, moves);
            }
            catch (Exception ex) when (FolderRenameJournal.IsFileSystemError(ex))
            {
                _logger.Error("Umbenennen fehlgeschlagen, nehme {Count} Umbenennungen zurück: {Reason}", ex, moves.Count, ex.Message);
                RollBack(moves);
                journal.Delete();
                throw;
            }

            journal.Delete();
            _logger.Info(
                "Folgenordner umbenannt: {Count} in '{Series}', {Skipped} übersprungen",
                moves.Count, Path.GetFileName(plan.SeriesFolderPath), skipped);
            return new FolderRenameResult(moves, skipped);
        }

        /// <summary>
        /// Benennt der Reihe nach um und hält jeden Schritt vorher im Journal fest.
        /// </summary>
        /// <returns>Zahl der übersprungenen Ordner.</returns>
        private int RenameAll(IReadOnlyList<FolderRenameItem> renames, FolderRenameJournal journal, List<(string OldPath, string NewPath)> moves)
        {
            int skipped = 0;
            foreach (FolderRenameItem item in renames)
            {
                // Zwischen Vorschau und Ausführung kann sich der Ordner verändert haben. Lieber
                // stehen lassen, als einen fremden Ordner zu überschreiben.
                if (!Directory.Exists(item.CurrentPath) || TargetTakenByOther(item))
                {
                    _logger.Warning("Umbenennen übersprungen, Quelle fehlt oder Ziel ist vergeben: {Path}", PathRedactor.Redact(item.CurrentPath));
                    skipped++;
                    continue;
                }

                // Erst die Absicht festhalten, dann umbenennen. Bricht der Prozess zwischen
                // beidem ab, weiß die Wiederherstellung trotzdem, was zu tun ist.
                journal.Record(item.CurrentPath, item.TargetPath);
                Move(item.CurrentPath, item.TargetPath);
                moves.Add((item.CurrentPath, item.TargetPath));
            }

            return skipped;
        }

        private List<FolderRenameItem> PlanFolders(string[] episodeFolders, IReadOnlyList<int?> numbers, string pattern, EpisodeFolderParser parser)
        {
            List<FolderRenameItem> items = new(episodeFolders.Length);
            for (int i = 0; i < episodeFolders.Length; i++)
            {
                items.Add(PlanFolder(episodeFolders[i], numbers[i], pattern, parser));
            }

            return MarkDuplicateTargets(items);
        }

        /// <summary>
        /// Plant einen einzelnen Ordner: Nummer prüfen, Titel holen, Namen bilden, Ziel prüfen.
        /// </summary>
        private FolderRenameItem PlanFolder(string folder, int? number, string pattern, EpisodeFolderParser parser)
        {
            string currentName = Path.GetFileName(folder);
            if (number is not int episodeNumber)
            {
                return new FolderRenameItem(folder, currentName, FolderRenameStatus.NoNumber, TitleFromTag: false);
            }

            (string title, bool fromTag) = ResolveTitle(folder, currentName, episodeNumber, parser);
            string? targetName = title.Length == 0 ? null : EpisodeFolderNaming.Render(pattern, episodeNumber, title);
            if (string.IsNullOrEmpty(targetName))
            {
                return new FolderRenameItem(folder, currentName, FolderRenameStatus.NoTitle, fromTag);
            }

            FolderRenameItem item = new(folder, targetName, FolderRenameStatus.Rename, fromTag);
            if (string.Equals(currentName, targetName, StringComparison.Ordinal))
            {
                return item with { Status = FolderRenameStatus.Unchanged };
            }

            if (item.TargetPath.Length > MaxDirectoryPathLength)
            {
                return item with { Status = FolderRenameStatus.TooLong };
            }

            return TargetTakenByOther(item) ? item with { Status = FolderRenameStatus.Conflict } : item;
        }

        /// <summary>
        /// Titel aus dem Album-Tag der ersten Datei, die einen trägt; fehlt er überall, der
        /// Titelteil des Ordnernamens.
        /// </summary>
        /// <remarks>
        /// Unterscheidet sich der Tag vom Ordnertitel nur in Groß- und Kleinschreibung, Satzzeichen
        /// oder Leerzeichen, gilt der Ordnertitel. Tags sind oft nachlässiger geschrieben
        /// („die Handy-Falle", „Betrug Beim Casting") als der Ordner, den jemand von Hand benannt
        /// hat; umbenannt wird nur bei einer echten Änderung des Titels.
        /// </remarks>
        private (string Title, bool FromTag) ResolveTitle(string folder, string currentName, int episodeNumber, EpisodeFolderParser parser)
        {
            string seriesName = Path.GetFileName(Path.GetDirectoryName(folder)) ?? string.Empty;
            string folderPart = parser.TryParse(currentName, out _, out string? parsedTitle) && !string.IsNullOrWhiteSpace(parsedTitle)
                ? parsedTitle
                : currentName;
            string folderTitle = EpisodeFolderNaming.CleanAlbumTitle(folderPart, episodeNumber, seriesName);

            string? tagTitle = ReadTagTitle(folder, episodeNumber, seriesName);
            return tagTitle is null || EpisodeFolderNaming.SameTitle(tagTitle, folderTitle)
                ? (folderTitle, false)
                : (tagTitle, true);
        }

        private string? ReadTagTitle(string folder, int episodeNumber, string seriesName)
        {
            // Dieselbe Regel wie beim Einlesen: Bei Folgen auf mehreren CDs liegen die Dateien in Unterordnern.
            foreach (string track in EpisodeAudioFiles.Collect(folder))
            {
                string? album = TryReadAlbum(track);
                string cleaned = album is null ? string.Empty : EpisodeFolderNaming.CleanAlbumTitle(album, episodeNumber, seriesName);
                if (cleaned.Length > 0)
                {
                    return cleaned;
                }
            }

            return null;
        }

        private string? TryReadAlbum(string track)
        {
            try
            {
                (string _, string album) = _tagTitleReader.Read(track);
                return string.IsNullOrWhiteSpace(album) ? null : album;
            }
            catch (Exception ex) when (FolderRenameJournal.IsFileSystemError(ex) || ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
            {
                _logger.Warning("Album-Tag nicht lesbar, nächste Datei: {Path} – {Reason}", PathRedactor.Redact(track), ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Zwei Ordner derselben Vorschau mit demselben Ziel: keiner wird umbenannt, sonst
        /// überschriebe der zweite den ersten.
        /// </summary>
        private static List<FolderRenameItem> MarkDuplicateTargets(List<FolderRenameItem> items)
        {
            HashSet<string> duplicates = [.. items
                .Where(item => item.Status is FolderRenameStatus.Rename or FolderRenameStatus.Unchanged)
                .GroupBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)];

            return [.. items.Select(item =>
                item.Status == FolderRenameStatus.Rename && duplicates.Contains(item.TargetName)
                    ? item with { Status = FolderRenameStatus.Conflict }
                    : item)];
        }

        /// <summary>
        /// Ob unter dem Zielnamen schon etwas anderes liegt. Derselbe Ordner in anderer
        /// Groß- und Kleinschreibung zählt nicht; Windows meldet ihn als vorhanden.
        /// </summary>
        private static bool TargetTakenByOther(FolderRenameItem item)
        {
            bool sameFolder = string.Equals(item.CurrentPath, item.TargetPath, StringComparison.OrdinalIgnoreCase);
            return !sameFolder && (Directory.Exists(item.TargetPath) || File.Exists(item.TargetPath));
        }

        /// <summary>
        /// Benennt um. Ändert sich nur die Groß- und Kleinschreibung, geht es über einen
        /// Zwischennamen, weil Windows beide Namen für denselben Ordner hält.
        /// </summary>
        private static void Move(string source, string target)
        {
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                string intermediate = source + ".echoplay-rename";
                Directory.Move(source, intermediate);
                Directory.Move(intermediate, target);
                return;
            }

            Directory.Move(source, target);
        }

        private void RollBack(List<(string OldPath, string NewPath)> moves)
        {
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                try
                {
                    Move(moves[i].NewPath, moves[i].OldPath);
                }
                catch (Exception ex) when (FolderRenameJournal.IsFileSystemError(ex))
                {
                    _logger.Error("Zurückbenennen fehlgeschlagen: {Path} – {Reason}", ex, PathRedactor.Redact(moves[i].NewPath), ex.Message);
                }
            }
        }

        private static FolderRenameItem Unchanged(string folder)
            => new(folder, Path.GetFileName(folder), FolderRenameStatus.Unchanged, TitleFromTag: false);
    }
}
