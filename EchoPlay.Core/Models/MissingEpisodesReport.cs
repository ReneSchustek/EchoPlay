using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Gesamtbericht über fehlende Folgen aller geprüften Serien.
    /// Enthält pro Serie die lokalen Lücken und die online verfügbaren Folgen
    /// nach der höchsten lokalen Nummer.
    /// </summary>
    public sealed class MissingEpisodesReport
    {
        /// <summary>Zeitpunkt der Prüfung (UTC).</summary>
        public required DateTime CheckedAtUtc { get; init; }

        /// <summary>Einzelergebnisse pro Serie, sortiert nach Serientitel.</summary>
        public required IReadOnlyList<SeriesMissingEpisodesResult> Results { get; init; }

        /// <summary>
        /// Gesamtzahl fehlender Folgen (lokale Lücken) über alle Serien.
        /// </summary>
        public int TotalLocalGaps
        {
            get
            {
                int total = 0;
                foreach (SeriesMissingEpisodesResult r in Results)
                {
                    total += r.LocalGaps.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// Gesamtzahl online verfügbarer Folgen über alle Serien.
        /// </summary>
        public int TotalOnlineNew
        {
            get
            {
                int total = 0;
                foreach (SeriesMissingEpisodesResult r in Results)
                {
                    total += r.OnlineEpisodes.Count;
                }
                return total;
            }
        }
    }

    /// <summary>
    /// Ergebnis der Fehlende-Folgen-Prüfung für eine einzelne Serie.
    /// Kombiniert lokale Dateisystem-Analyse mit Live-Online-Abgleich.
    /// </summary>
    public sealed class SeriesMissingEpisodesResult
    {
        /// <summary>Anzeigename der Serie.</summary>
        public required string SeriesTitle { get; init; }

        /// <summary>Höchste lokal vorhandene Folgennummer (aus Ordnernamen).</summary>
        public int LocalHighestNumber { get; init; }

        /// <summary>
        /// Höchste online verfügbare Folgennummer (aus iTunes/Apple Music).
        /// 0, wenn kein Online-Abgleich möglich war.
        /// </summary>
        public int OnlineHighestNumber { get; init; }

        /// <summary>
        /// Fehlende Folgennummern in der lokalen Nummerierung (Lücken).
        /// Leer, wenn alle Folgen von 1 bis <see cref="LocalHighestNumber"/> vorhanden sind.
        /// </summary>
        public required IReadOnlyList<int> LocalGaps { get; init; }

        /// <summary>
        /// Online verfügbare Folgen nach der höchsten lokalen Nummer.
        /// Jeder Eintrag enthält Nummer und Titel aus iTunes.
        /// Leer, wenn kein Online-Abgleich möglich war oder keine neuen Folgen existieren.
        /// </summary>
        public required IReadOnlyList<OnlineEpisodeInfo> OnlineEpisodes { get; init; }

        /// <summary>
        /// Fehlermeldung, falls die Prüfung dieser Serie fehlgeschlagen ist.
        /// Null bei erfolgreicher Prüfung.
        /// </summary>
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Eine online verfügbare Folge, die lokal nicht vorhanden ist.
    /// </summary>
    public sealed class OnlineEpisodeInfo
    {
        /// <summary>Folgennummer (extrahiert aus dem iTunes-Albumnamen).</summary>
        public int EpisodeNumber { get; init; }

        /// <summary>Albumname aus iTunes (z.B. "Die drei ??? - Folge 230 - Titel").</summary>
        public required string Title { get; init; }
    }

    /// <summary>
    /// Formatiert einen <see cref="MissingEpisodesReport"/> als lesbaren Klartext.
    /// Wird für den TXT-Export und die Dialog-Anzeige verwendet.
    /// </summary>
    public static class MissingEpisodesReportFormatter
    {
        /// <summary>
        /// Formatiert den Bericht als TXT-Datei-Inhalt.
        /// </summary>
        /// <param name="report">Der zu formatierende Bericht.</param>
        /// <returns>Formatierter Klartext mit Überschrift, Serienblöcken und Zusammenfassung.</returns>
        public static string FormatAsText(MissingEpisodesReport report)
        {
            StringBuilder sb = new();

            string dateStr = report.CheckedAtUtc.ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            sb.AppendLine(CultureInfo.InvariantCulture, $"Fehlende Folgen – EchoPlay ({dateStr})");
            sb.AppendLine("===================================");
            sb.AppendLine();

            foreach (SeriesMissingEpisodesResult result in report.Results)
            {
                FormatSeriesBlock(sb, result);
                sb.AppendLine();
            }

            sb.AppendLine("===================================");
            sb.Append(CultureInfo.InvariantCulture,
                $"Geprüft: {report.Results.Count} Serien | " +
                $"Lokale Lücken: {report.TotalLocalGaps} | " +
                $"Online neu: {report.TotalOnlineNew}");

            return sb.ToString();
        }

        /// <summary>
        /// Formatiert einen einzelnen Serienblock.
        /// </summary>
        private static void FormatSeriesBlock(StringBuilder sb, SeriesMissingEpisodesResult result)
        {
            if (result.ErrorMessage is not null)
            {
                sb.AppendLine(result.SeriesTitle);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Fehler: {result.ErrorMessage}");
                return;
            }

            // Überschrift mit Nummernbereich
            string range = result.LocalHighestNumber > 0
                ? FormattableString.Invariant($" (lokal: 1–{result.LocalHighestNumber})")
                : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture, $"{result.SeriesTitle}{range}");

            // Lokale Lücken
            if (result.LocalGaps.Count > 0)
            {
                List<string> gapStrings = new(result.LocalGaps.Count);
                foreach (int gap in result.LocalGaps)
                {
                    gapStrings.Add(FormattableString.Invariant($"Folge {gap:D3}"));
                }
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Lokale Lücken: {string.Join(", ", gapStrings)}");
            }
            else if (result.LocalHighestNumber > 0)
            {
                sb.AppendLine("  Keine lokalen Lücken.");
            }

            // Online verfügbare Folgen
            if (result.OnlineEpisodes.Count > 0)
            {
                foreach (OnlineEpisodeInfo ep in result.OnlineEpisodes)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"  Online verfügbar: Folge {ep.EpisodeNumber:D3} – {ep.Title}");
                }
            }

            // Alles komplett
            if (result.LocalGaps.Count == 0 && result.OnlineEpisodes.Count == 0
                && result.LocalHighestNumber > 0)
            {
                sb.AppendLine("  Alle Folgen komplett.");
            }
        }
    }
}
