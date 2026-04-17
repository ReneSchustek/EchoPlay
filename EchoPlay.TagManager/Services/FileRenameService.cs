using EchoPlay.Logger.Abstractions;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.TagManager.Services
{
    /// <summary>
    /// Benennt Audiodateien nach einem konfigurierbaren Platzhaltermuster um.
    /// Die Platzhalter-Syntax orientiert sich an MP3Tag, verwendet aber geschweifte Klammern
    /// konsistent mit dem übrigen EchoPlay-Dateinamensystem.
    ///
    /// Auflösungsreihenfolge der Track-Platzhalter: {track:000} und {track:00} werden vor
    /// {track} ersetzt, damit {track} nicht versehentlich den Anfang von {track:00} matcht.
    /// </summary>
    internal sealed class FileRenameService : IFileRenameService
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Service mit einer Logger-Fabrik.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public FileRenameService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(nameof(FileRenameService));
        }

        /// <inheritdoc/>
        public IReadOnlyList<RenamePreviewItem> BuildPreview(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
        {
            List<RenamePreviewItem> items = new(files.Count);

            foreach ((string filePath, AudioTag tag) in files)
            {
                string oldName = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath);
                string directory = Path.GetDirectoryName(filePath) ?? string.Empty;

                string resolvedBase = ResolvePattern(pattern, filePath, tag);
                string newName = resolvedBase + extension;
                string newFilePath = Path.Combine(directory, newName);

                items.Add(new RenamePreviewItem
                {
                    OldName = oldName,
                    NewName = newName,
                    FilePath = filePath,
                    NewFilePath = newFilePath
                });
            }

            return items;
        }

        /// <inheritdoc/>
        public async Task<int> RenameAsync(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
        {
            IReadOnlyList<RenamePreviewItem> preview = BuildPreview(files, pattern);
            int successCount = 0;

            foreach (RenamePreviewItem item in preview)
            {
                // Unveränderte Dateien überspringen – File.Move auf denselben Namen würde sonst fehlschlagen
                if (item.IsUnchanged)
                {
                    continue;
                }

                try
                {
                    // File.Move ist auf demselben Laufwerk atomar – kein Zwischenkopieren nötig
                    await Task.Run(() => File.Move(item.FilePath, item.NewFilePath, overwrite: false)).ConfigureAwait(false);
                    _logger.Debug($"Umbenannt: \"{item.OldName}\" → \"{item.NewName}\"");
                    successCount++;
                }
                catch (IOException ex)
                {
                    // Gesperrte Dateien oder Namenskonflikte werden übersprungen und geloggt
                    _logger.Warning($"Umbenennung fehlgeschlagen für \"{item.OldName}\": {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.Warning($"Zugriff verweigert für \"{item.OldName}\": {ex.Message}");
                }
            }

            _logger.Info($"Umbenennung abgeschlossen: {successCount}/{preview.Count} Dateien erfolgreich");

            return successCount;
        }

        /// <summary>
        /// Löst alle Platzhalter im Muster anhand der Tag-Daten und des Dateipfads auf.
        /// Ungültige Zeichen für Dateinamen werden durch Unterstriche ersetzt.
        /// </summary>
        /// <param name="pattern">Muster mit Platzhaltern.</param>
        /// <param name="filePath">Vollständiger Pfad der Quelldatei.</param>
        /// <param name="tag">Metadaten der Datei.</param>
        /// <returns>Aufgelöster Dateiname ohne Extension und ohne ungültige Zeichen.</returns>
        private static string ResolvePattern(string pattern, string filePath, AudioTag tag)
        {
            string result = pattern;

            // Einfache Felder – leerer String wenn Tag nicht gesetzt
            result = result.Replace("{title}", tag.Title ?? string.Empty, StringComparison.Ordinal);
            result = result.Replace("{album}", tag.Album ?? string.Empty, StringComparison.Ordinal);
            result = result.Replace("{artist}", tag.Artist ?? string.Empty, StringComparison.Ordinal);
            result = result.Replace("{year}", tag.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal);
            result = result.Replace("{filename}", Path.GetFileNameWithoutExtension(filePath), StringComparison.Ordinal);

            // Tracknummer: formatierten Varianten zuerst ersetzen, damit {track} nicht
            // versehentlich den Anfang von {track:00} matched
            if (tag.TrackNumber.HasValue)
            {
                result = result.Replace("{track:000}", tag.TrackNumber.Value.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal);
                result = result.Replace("{track:00}", tag.TrackNumber.Value.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal);
                result = result.Replace("{track}", tag.TrackNumber.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }
            else
            {
                result = result.Replace("{track:000}", string.Empty, StringComparison.Ordinal);
                result = result.Replace("{track:00}", string.Empty, StringComparison.Ordinal);
                result = result.Replace("{track}", string.Empty, StringComparison.Ordinal);
            }

            return SanitizeFileName(result);
        }

        /// <summary>
        /// Ersetzt alle für Dateinamen ungültigen Zeichen durch Unterstriche
        /// und trimmt führende und abschließende Leerzeichen.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }
    }
}
