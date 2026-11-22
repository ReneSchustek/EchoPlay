using EchoPlay.App.Helpers;
using EchoPlay.LocalLibrary.Models;
using System.Globalization;
using System.Text;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Schreibt die Vorschau und das Ergebnis des Ordner-Umbenennens als Text für den Dialog.
    /// </summary>
    /// <remarks>
    /// Was nicht umbenannt wird, steht mit Grund darunter. Wer eine Sonderfolge oder einen
    /// Konflikt nicht sieht, hält das Werkzeug sonst für fehlerhaft, weil ein Ordner seinen
    /// Namen behält.
    /// </remarks>
    internal static class FolderRenamePreviewText
    {
        /// <summary>
        /// Baut den Vorschautext.
        /// </summary>
        /// <param name="plan">Die Vorschau des Dienstes.</param>
        /// <returns>Der Text für den Dialog.</returns>
        public static string Build(FolderRenamePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (!plan.PatternSupported)
            {
                return SafeResourceLoader.Get(
                    "FolderRenamePatternUnsupported",
                    "Das eingestellte Ordnermuster enthält Platzhalter, aus denen sich kein Name bilden lässt. Erlaubt sind {number}, {number:000} und {title}.");
            }

            if (!plan.SeriesIsNumbered)
            {
                return SafeResourceLoader.Get(
                    "FolderRenameSeriesUnnumbered",
                    "Kein Ordner dieser Serie trägt eine Folgennummer. Ohne Nummer lässt sich kein Name nach dem Muster bilden; die Ordner bleiben, wie sie sind.");
            }

            StringBuilder text = new();
            if (plan.RecoveredCount > 0)
            {
                string recovered = SafeResourceLoader.Get("FolderRenameRecovered", "Ein abgebrochener Lauf wurde zurückgenommen: {0} Ordner tragen wieder ihren alten Namen.");
                _ = text.AppendLine(Fill(recovered, plan.RecoveredCount)).AppendLine();
            }

            AppendRenames(text, plan.Renames);

            AppendSection(text, plan, FolderRenameStatus.Conflict, "FolderRenameSectionConflict", "Ziel schon vergeben, bleibt:");
            AppendSection(text, plan, FolderRenameStatus.TooLong, "FolderRenameSectionTooLong", "Neuer Pfad zu lang, bleibt:");
            AppendSection(text, plan, FolderRenameStatus.NoNumber, "FolderRenameSectionNoNumber", "Ohne Folgennummer (Sonderfolge), bleibt:");
            AppendSection(text, plan, FolderRenameStatus.NoTitle, "FolderRenameSectionNoTitle", "Kein Titel im Tag und im Ordnernamen, bleibt:");
            return text.ToString().TrimEnd();
        }

        /// <summary>Die Umbenennungen: je Ordner alter und neuer Name, mit Hinweis, wenn der Titel aus dem Ordnernamen stammt.</summary>
        private static void AppendRenames(StringBuilder text, IReadOnlyList<FolderRenameItem> renames)
        {
            if (renames.Count == 0)
            {
                _ = text.AppendLine(SafeResourceLoader.Get("FolderRenameNothingToDo", "Alle Ordner heißen schon nach dem Muster."));
                return;
            }

            string header = SafeResourceLoader.Get("FolderRenameHeader", "{0} Ordner werden umbenannt:");
            _ = text.AppendLine(Fill(header, renames.Count)).AppendLine();
            string titleFromFolder = SafeResourceLoader.Get("FolderRenameTitleFromFolder", "(Titel aus dem Ordnernamen)");
            foreach (FolderRenameItem item in renames)
            {
                _ = text.Append("  ").AppendLine(item.CurrentName);
                _ = text.Append("    → ").Append(item.TargetName);
                _ = item.TitleFromTag ? text.AppendLine() : text.Append("  ").AppendLine(titleFromFolder);
            }
        }

        /// <summary>
        /// Baut die Meldung nach dem Umbenennen.
        /// </summary>
        /// <param name="result">Das Ergebnis des Laufs.</param>
        /// <returns>Der Text für den Dialog.</returns>
        public static string BuildResult(FolderRenameResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string done = Fill(SafeResourceLoader.Get("FolderRenameResultDone", "{0} Ordner umbenannt."), result.Moves.Count);
            if (result.SkippedCount == 0)
            {
                return done;
            }

            string skipped = SafeResourceLoader.Get("FolderRenameResultSkipped", "{0} Ordner übersprungen: Sie haben sich seit der Vorschau verändert.");
            return done + Environment.NewLine + Fill(skipped, result.SkippedCount);
        }

        // Das Muster kommt zur Laufzeit aus den Sprachressourcen; ein zwischengespeichertes
        // CompositeFormat brächte bei wechselnder Sprache nichts.
        private static string Fill(string pattern, int count)
            => string.Format(CultureInfo.CurrentCulture, pattern, count);

        private static void AppendSection(StringBuilder text, FolderRenamePlan plan, FolderRenameStatus status, string key, string fallback)
        {
            FolderRenameItem[] items = [.. plan.Items.Where(item => item.Status == status)];
            if (items.Length == 0)
            {
                return;
            }

            _ = text.AppendLine().AppendLine(SafeResourceLoader.Get(key, fallback));
            foreach (FolderRenameItem item in items)
            {
                _ = text.Append("  ").AppendLine(item.CurrentName);
            }
        }
    }
}
