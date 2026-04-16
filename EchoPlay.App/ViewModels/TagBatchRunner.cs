using EchoPlay.App.Helpers;
using EchoPlay.App.Services;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Geteilte Hilfsfunktionen für die Tag-Manager-Sub-Actions: Generischer Batch-Runner
    /// mit Fortschrittsanzeige sowie die Merge-Regeln für Einzel- und Mehrfach-Speichern.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
    internal static class TagBatchRunner
    {
        /// <summary>
        /// Führt eine Batch-Operation mit Fortschrittsanzeige und Fehlerdialog aus.
        /// Setzt <c>IsLoading</c>, aktualisiert <c>BatchProgressText</c> und räumt im Finally auf.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Generischer Batch-Runner fuer Tag-Operationen auf mehreren Dateien: TagLib-/IO-Fehler einer einzelnen Datei duerfen den Batch nicht abbrechen; Einzelfehler werden in der Fehlerliste gesammelt und am Ende angezeigt.")]
        public static async Task RunBatchAsync(
            IReadOnlyList<TagFileItemViewModel> files,
            Func<TagFileItemViewModel, Task> perFile,
            string errorTitle,
            IErrorDialogService errorDialogService,
            Action<bool> setIsLoading,
            Action<string> setBatchProgress)
        {
            setIsLoading(true);
            int processed = 0;

            try
            {
                foreach (TagFileItemViewModel file in files)
                {
                    processed++;
                    setBatchProgress(string.Format(
                        CultureInfo.CurrentCulture,
                        SafeResourceLoader.Get("TagManagerBatchProgressText"),
                        processed, files.Count));

                    await perFile(file);
                }
            }
            catch (Exception ex)
            {
                await errorDialogService.ShowAsync(errorTitle, ex.Message);
            }
            finally
            {
                setBatchProgress(string.Empty);
                setIsLoading(false);
            }
        }

        /// <summary>
        /// Verschmilzt nur die vom Nutzer geänderten Felder in die bestehenden Tags einer Datei.
        /// </summary>
        public static AudioTag MergeEditedIntoExisting(AudioTag edited, AudioTag existing)
        {
            return new AudioTag
            {
                Title       = edited.Title       ?? existing.Title,
                Album       = edited.Album       ?? existing.Album,
                Artist      = edited.Artist      ?? existing.Artist,
                AlbumArtist = edited.AlbumArtist ?? existing.AlbumArtist,
                Genre       = edited.Genre       ?? existing.Genre,
                Year        = edited.Year        ?? existing.Year,
                TrackNumber = edited.TrackNumber ?? existing.TrackNumber,
                TrackCount  = edited.TrackCount  ?? existing.TrackCount
            };
        }

        /// <summary>
        /// Verschmilzt gemeinsame Tags in die bestehenden Tags einer Datei.
        /// Title und TrackNumber stammen aus der Datei, alle anderen Felder aus dem Batch-Tag.
        /// </summary>
        public static AudioTag MergeSharedIntoExisting(AudioTag shared, AudioTag existing)
        {
            return new AudioTag
            {
                Title       = existing.Title,
                TrackNumber = existing.TrackNumber,
                Album       = shared.Album       ?? existing.Album,
                Artist      = shared.Artist      ?? existing.Artist,
                AlbumArtist = shared.AlbumArtist ?? existing.AlbumArtist,
                Genre       = shared.Genre       ?? existing.Genre,
                Year        = shared.Year        ?? existing.Year,
                TrackCount  = shared.TrackCount  ?? existing.TrackCount
            };
        }
    }
}
