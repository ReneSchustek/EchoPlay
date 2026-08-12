using EchoPlay.Logger.Models;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Kriterien, nach denen die Protokollansicht ihre Meldungen einschränkt: ein Suchtext
    /// und eine Stufe.
    /// <para>
    /// Als eigener Typ, damit das Protokoll-ViewModel neben dem Live-Empfang vom Logger nicht
    /// auch noch die Frage „passt diese Meldung" mitträgt.
    /// </para>
    /// </summary>
    public sealed class LogEntryFilter
    {
        /// <summary>Freitext über Meldung und Bereich. Leer heißt: keine Einschränkung.</summary>
        public string SearchText { get; set; } = string.Empty;

        /// <summary>Gewählte Stufe oder <see langword="null"/> für alle Stufen.</summary>
        public LogLevel? Level { get; set; }

        /// <summary>Ob überhaupt eingeschränkt wird.</summary>
        public bool IsActive => !string.IsNullOrWhiteSpace(SearchText) || Level is not null;

        /// <summary>Setzt alle Kriterien zurück.</summary>
        public void Reset()
        {
            SearchText = string.Empty;
            Level = null;
        }

        /// <summary>
        /// Prüft eine Meldung gegen Suchtext und Stufe.
        /// <para>
        /// Die Stufe filtert genau, nicht ab einer Schwelle: Wer nach Warnungen sucht, will
        /// Warnungen sehen und nicht zusätzlich jeden Fehler.
        /// </para>
        /// </summary>
        /// <param name="entry">Die zu prüfende Meldung.</param>
        /// <returns><see langword="true"/>, wenn die Meldung sichtbar bleibt.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> ist <see langword="null"/>.</exception>
        public bool Matches(LogEntryViewModel entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (Level is not null && entry.Level != Level)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return entry.Message.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                || entry.Category.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
