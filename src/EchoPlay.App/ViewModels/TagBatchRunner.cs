using EchoPlay.App.Helpers;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
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
    internal static class TagBatchRunner
    {
        /// <summary>
        /// Führt eine Batch-Operation mit Fortschrittsanzeige und Fehlerdialog aus.
        /// Setzt <c>IsLoading</c>, aktualisiert <c>BatchProgressText</c> und räumt im Finally auf.
        /// Jede Datei wird versucht; was scheitert, steht am Ende gesammelt in einem Dialog.
        /// </summary>
        /// <param name="files">Die zu bearbeitenden Dateien.</param>
        /// <param name="perFile">Die Arbeit an einer Datei.</param>
        /// <param name="errorTitle">Titel des Fehlerdialogs.</param>
        /// <param name="errorKey">Kennung des Fehlerdialogs für das Ausblenden.</param>
        /// <param name="errorDialogService">Zeigt die gesammelten Fehler.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="setBatchProgress">Setzt den Fortschrittstext.</param>
        /// <returns>Wie viele Dateien gescheitert sind.</returns>
        public static async Task<int> RunBatchAsync(
            IReadOnlyList<TagFileItemViewModel> files,
            Func<TagFileItemViewModel, Task> perFile,
            string errorTitle,
            DialogKey errorKey,
            IErrorDialogService errorDialogService,
            Action<bool> setIsLoading,
            Action<string> setBatchProgress)
        {
            setIsLoading(true);
            List<string> failures;

            try
            {
                failures = await ProcessEachAsync(files, perFile, setBatchProgress);
            }
            finally
            {
                setBatchProgress(string.Empty);
                setIsLoading(false);
            }

            if (failures.Count > 0)
            {
                await errorDialogService.ShowAsync(errorTitle, string.Join(Environment.NewLine, failures), errorKey);
            }

            return failures.Count;
        }

        /// <summary>
        /// Bearbeitet jede Datei und meldet den Fortschritt; Fehler einzelner Dateien werden gesammelt.
        /// </summary>
        /// <returns>Je gescheiterte Datei eine Zeile mit Namen und Grund.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Generischer Batch-Runner für Tag-Operationen auf mehreren Dateien: TagLib-/IO-Fehler einer einzelnen Datei dürfen den Batch nicht abbrechen; Einzelfehler werden gesammelt und am Ende angezeigt.")]
        private static async Task<List<string>> ProcessEachAsync(
            IReadOnlyList<TagFileItemViewModel> files,
            Func<TagFileItemViewModel, Task> perFile,
            Action<string> setBatchProgress)
        {
            string progressPattern = SafeResourceLoader.Get("TagManagerBatchProgressText");
            List<string> failures = [];
            int processed = 0;

            foreach (TagFileItemViewModel file in files)
            {
                processed++;
                setBatchProgress(string.Format(CultureInfo.CurrentCulture, progressPattern, processed, files.Count));

                try
                {
                    await perFile(file);
                }
                catch (Exception ex)
                {
                    // Eine gesperrte oder beschädigte Datei hält die übrigen nicht auf.
                    failures.Add($"{file.FileName}: {ex.Message}");
                }
            }

            return failures;
        }

        /// <summary>
        /// Verschmilzt nur die vom Nutzer geänderten Felder in die bestehenden Tags einer Datei.
        /// </summary>
        public static AudioTag MergeEditedIntoExisting(AudioTag edited, AudioTag existing)
        {
            return new AudioTag
            {
                Title = edited.Title ?? existing.Title,
                Album = edited.Album ?? existing.Album,
                Artist = edited.Artist ?? existing.Artist,
                AlbumArtist = edited.AlbumArtist ?? existing.AlbumArtist,
                Genre = edited.Genre ?? existing.Genre,
                Year = edited.Year ?? existing.Year,
                TrackNumber = edited.TrackNumber ?? existing.TrackNumber,
                TrackCount = edited.TrackCount ?? existing.TrackCount
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
                Title = existing.Title,
                TrackNumber = existing.TrackNumber,
                Album = shared.Album ?? existing.Album,
                Artist = shared.Artist ?? existing.Artist,
                AlbumArtist = shared.AlbumArtist ?? existing.AlbumArtist,
                Genre = shared.Genre ?? existing.Genre,
                Year = shared.Year ?? existing.Year,
                TrackCount = shared.TrackCount ?? existing.TrackCount
            };
        }
    }
}
