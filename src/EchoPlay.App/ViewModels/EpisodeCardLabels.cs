using EchoPlay.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Texte einer Folgen-Kachel auf der Startseite: bereinigter Titel, Info-Zeile und Abzeichen.
    /// </summary>
    internal static partial class EpisodeCardLabels
    {
        // Wie lange eine erschienene Folge das Abzeichen „Neu" trägt.
        private const int NewBadgeDays = 7;

        /// <summary>
        /// Info-Zeile für das Kachel-Overlay im einheitlichen Format:
        /// „Nr. 170 · 03.04.2026 · online" (Neuerscheinung),
        /// „Nr. 26 · 25.04.2026 · angekündigt" (Ankündigung).
        /// </summary>
        /// <param name="localization">Liefert die Textbausteine; ohne sie gelten die deutschen.</param>
        /// <param name="episodeNumber">Folgennummer, sofern bekannt.</param>
        /// <param name="releaseDate">Erscheinungsdatum; ohne Datum gibt es keine Info-Zeile.</param>
        /// <param name="isAnnounced">Ob die Folge nur angekündigt ist.</param>
        /// <param name="hasLocalTrack">Ob die Folge lokal vorliegt.</param>
        /// <returns>Die Zeile, oder <see langword="null"/> ohne Datum.</returns>
        public static string? InfoLine(
            ILocalizationService? localization, int? episodeNumber, DateTime? releaseDate, bool isAnnounced, bool hasLocalTrack)
        {
            if (!releaseDate.HasValue)
            {
                return null;
            }

            List<string> parts = [];

            if (episodeNumber.HasValue)
            {
                string numberPattern = localization?.Get("NewEpisodeInfoNumberFormat") ?? "Nr. {0}";
                parts.Add(string.Format(CultureInfo.CurrentCulture, numberPattern, episodeNumber.Value));
            }

            parts.Add(releaseDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));

            // Mitten in der Zeile klein geschrieben; die Schreibweise kommt aus der Ressource,
            // weil nicht jede Sprache ein Wort am Satzanfang anders schreibt als mittendrin.
            if (isAnnounced)
            {
                parts.Add(localization?.Get("NewEpisodeInfoAnnounced") ?? "angekündigt");
            }
            else if (!hasLocalTrack)
            {
                parts.Add(localization?.Get("NewEpisodeInfoOnline") ?? "online");
            }

            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Das Abzeichen der Kachel: Angekündigt vor Neu (bis sieben Tage nach Erscheinen), sonst
        /// keines. Text und Sichtbarkeit stehen unabhängig von WinUI; nur die Farbe kommt aus der
        /// Palette und ist deshalb ohne laufende Anwendung nicht auflösbar.
        /// </summary>
        /// <param name="localization">Liefert die Texte; ohne sie gelten die deutschen.</param>
        /// <param name="isAnnounced">Ob die Folge nur angekündigt ist.</param>
        /// <param name="releaseDate">Erscheinungsdatum, sofern bekannt.</param>
        /// <param name="today">Der heutige Tag (UTC).</param>
        /// <returns>Text, Sichtbarkeit und Farbe des Abzeichens.</returns>
        public static (string? Text, Visibility Visibility, Brush? Brush) Badge(
            ILocalizationService? localization, bool isAnnounced, DateTime? releaseDate, DateTime today)
        {
            if (isAnnounced)
            {
                return (localization?.Get("BadgeAnnounced") ?? "Angekündigt", Visibility.Visible, TryResolveThemeBrush("AccentPrimaryBrush"));
            }

            if (releaseDate.HasValue && (today - releaseDate.Value.Date).TotalDays <= NewBadgeDays)
            {
                return (localization?.Get("BadgeNew") ?? "Neu", Visibility.Visible, TryResolveThemeBrush("StatusSuccessBrush"));
            }

            return (null, Visibility.Collapsed, null);
        }

        /// <summary>
        /// Entfernt Serienname und Folgennummer-Prefix aus dem iTunes-Albumnamen,
        /// damit nur der reine Episodentitel übrig bleibt (z.B. "Zusammengewachsen"
        /// statt "Kira Kolumna - Folge 26 - Zusammengewachsen").
        /// </summary>
        /// <param name="title">Der Albumname vom Anbieter.</param>
        /// <param name="seriesName">Der Name der Serie.</param>
        /// <returns>Der reine Titel; bleibt nichts übrig, der Albumname.</returns>
        public static string CleanEpisodeTitle(string title, string seriesName)
        {
            string cleaned = title;

            // Serienname am Anfang entfernen (z.B. "Kira Kolumna - ...")
            if (cleaned.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[seriesName.Length..].TrimStart(' ', '-', '–', '—', ':');
            }

            // Folgennummer-Prefix entfernen:
            // "Folge 26 - Titel", "170 - Titel", "Folge 170: Titel", "26: Titel"
            Match match = EpisodeNumberPrefix().Match(cleaned);
            if (match.Success)
            {
                cleaned = cleaned[match.Length..];
            }

            // Falls nichts übrig bleibt, Originaltitel beibehalten
            return string.IsNullOrWhiteSpace(cleaned) ? title : cleaned;
        }

        /// <summary>
        /// Löst einen Farbschlüssel der Palette auf.
        /// <para>
        /// Über den Schlüssel und nicht über einen Farbwert im Quelltext: Der Nutzer wählt
        /// zwischen sechs Paletten, und eine hier fest verdrahtete Farbe stünde in fünf davon
        /// daneben. Der Schlüsselsatz ist in allen Paletten vollständig. Dass er hier zur
        /// Laufzeit gesucht wird, ändert daran nichts.
        /// </para>
        /// </summary>
        /// <param name="key">Schlüssel aus der Palette, etwa <c>AccentPrimaryBrush</c>.</param>
        /// <returns>Der Pinsel, oder <see langword="null"/> ohne laufende Anwendung (Tests).</returns>
        private static Brush? TryResolveThemeBrush(string key)
        {
            try
            {
                return Application.Current.Resources[key] as Brush;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Ohne WinUI-Runtime gibt es keine Palette, die Kachel bleibt ohne Farbe.
                return null;
            }
        }

        /// <summary>
        /// Folgennummer am Titelanfang, etwa „Folge 26 - " oder „170: ". Der Titel kommt vom
        /// Anbieter, deshalb mit Zeitgrenze wie jeder Ausdruck auf fremden Text.
        /// </summary>
        [GeneratedRegex(@"^(?:Folge\s+)?\d+\s*[-–—:]\s*", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex EpisodeNumberPrefix();
    }
}
