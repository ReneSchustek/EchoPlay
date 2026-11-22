using EchoPlay.Core.Logging;
using EchoPlay.Logger.Abstractions;
using System.Security;
using System.Text.Json;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Das Journal eines Umbenennungslaufs im Serienordner. Es hält jeden Schritt fest, bevor er
    /// ausgeführt wird, damit ein Abbruch mittendrin sich zurückspielen lässt.
    /// </summary>
    internal sealed class FolderRenameJournal
    {
        /// <summary>Dateiname des Journals im Serienordner; nach einem vollständigen Lauf gelöscht.</summary>
        public const string FileName = ".echoplay-rename-journal.json";

        private readonly ILogger _logger;
        private readonly string _path;
        private readonly List<Entry> _entries = [];

        /// <summary>
        /// Richtet das Journal für einen Serienordner ein.
        /// </summary>
        /// <param name="seriesFolderPath">Der Serienordner, in dem das Journal liegt.</param>
        /// <param name="logger">Protokollkanal.</param>
        public FolderRenameJournal(string seriesFolderPath, ILogger logger)
        {
            _path = Path.Combine(seriesFolderPath, FileName);
            _logger = logger;
        }

        /// <summary>
        /// Hält einen Schritt fest. Scheitert das Schreiben, gibt es den Fehler weiter: Ohne
        /// Journal wäre ein Abbruch nach dem Umbenennen nicht mehr rückgängig zu machen.
        /// </summary>
        /// <param name="source">Pfad vor dem Umbenennen.</param>
        /// <param name="target">Pfad danach.</param>
        public void Record(string source, string target)
        {
            _entries.Add(new Entry(source, target));
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }

        /// <summary>Löscht das Journal nach einem vollständigen oder zurückgenommenen Lauf.</summary>
        public void Delete()
        {
            try
            {
                File.Delete(_path);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                _logger.Warning("Journal nicht löschbar: {Path} – {Reason}", PathRedactor.Redact(_path), ex.Message);
            }
        }

        /// <summary>
        /// Spielt ein liegengebliebenes Journal zurück: Jeder Ordner, der schon unter dem neuen
        /// Namen liegt, bekommt den alten wieder, in umgekehrter Reihenfolge.
        /// </summary>
        /// <param name="move">Benennt einen Ordner um; derselbe Weg wie beim Lauf.</param>
        /// <returns>Zahl der zurückbenannten Ordner; 0, wenn kein Journal da ist.</returns>
        public int Recover(Action<string, string> move)
        {
            ArgumentNullException.ThrowIfNull(move);

            List<Entry>? entries = TryRead();
            if (entries is null)
            {
                return 0;
            }

            int recovered = 0;
            foreach (Entry entry in Enumerable.Reverse(entries))
            {
                if (TryRestore(entry, move))
                {
                    recovered++;
                }
            }

            _logger.Info("Abgebrochenes Umbenennen zurückgespielt: {Count} Ordner in '{Series}'", recovered, Path.GetFileName(Path.GetDirectoryName(_path)));
            Delete();
            return recovered;
        }

        /// <summary>
        /// Liest das Journal. Ein unlesbares bleibt liegen, damit es sich von Hand prüfen lässt.
        /// </summary>
        private List<Entry>? TryRead()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
            }
            catch (Exception ex) when (IsFileSystemError(ex) || ex is JsonException)
            {
                _logger.Warning("Journal nicht lesbar, bleibt liegen: {Path} – {Reason}", PathRedactor.Redact(_path), ex.Message);
                return null;
            }
        }

        private bool TryRestore(Entry entry, Action<string, string> move)
        {
            if (!Directory.Exists(entry.Target) || Directory.Exists(entry.Source))
            {
                return false;
            }

            try
            {
                move(entry.Target, entry.Source);
                return true;
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                _logger.Error("Wiederherstellung fehlgeschlagen: {Path} – {Reason}", ex, PathRedactor.Redact(entry.Target), ex.Message);
                return false;
            }
        }

        /// <summary>Fehler des Dateisystems, die ein Umbenennen oder das Journal treffen können.</summary>
        /// <param name="ex">Die gefangene Ausnahme.</param>
        /// <returns><see langword="true"/>, wenn sie vom Dateisystem stammt.</returns>
        internal static bool IsFileSystemError(Exception ex) => ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or ArgumentException;

        /// <summary>Ein geplanter Schritt: alter und neuer Pfad.</summary>
        /// <param name="Source">Pfad vor dem Umbenennen.</param>
        /// <param name="Target">Pfad nach dem Umbenennen.</param>
        private sealed record Entry(string Source, string Target);
    }
}
