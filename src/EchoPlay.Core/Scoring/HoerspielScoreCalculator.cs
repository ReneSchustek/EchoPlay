namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Ein zusätzlicher, provider-spezifischer Score-Bestandteil (z.B. der Genre-Bonus
    /// von Apple Music), der in die gemeinsame Score-Arithmetik eingereiht wird.
    /// </summary>
    /// <param name="Condition">Ob der Bestandteil vorliegt.</param>
    /// <param name="Bonus">Der zu addierende Punktwert.</param>
    /// <param name="Label">Der Anzeigetext im Debug-String (ohne den "+Bonus"-Zusatz).</param>
    public readonly record struct HoerspielScoreComponent(bool Condition, int Bonus, string Label);

    /// <summary>
    /// Berechnet das <see cref="HoerspielScoreResult"/> aus den gemeinsamen Analyse-Flags.
    /// Kapselt die zwischen Spotify und Apple Music identische Arithmetik: harte Akzeptanz
    /// bekannter Serien, additive Boni und den Schwellwert-Vergleich. Provider-spezifische
    /// Boni werden über den <c>extraComponents</c>-Parameter eingereiht.
    /// </summary>
    public static class HoerspielScoreCalculator
    {
        /// <summary>
        /// Bewertet eine Analyse zum Score-Ergebnis.
        /// </summary>
        /// <param name="artistId">Die Artist-ID (Ergebnis-Schlüssel).</param>
        /// <param name="analysis">Die gemeinsamen Analyse-Flags.</param>
        /// <param name="settings">Die Bewertungs-Einstellungen (Boni, Schwellwert).</param>
        /// <param name="extraComponents">Optionale provider-spezifische Score-Bestandteile.</param>
        /// <returns>Das Bewertungsergebnis.</returns>
        public static HoerspielScoreResult Evaluate(
            string artistId,
            IHoerspielAnalysis analysis,
            HoerspielScorerSettings settings,
            IReadOnlyList<HoerspielScoreComponent>? extraComponents = null)
        {
            ArgumentNullException.ThrowIfNull(artistId);
            ArgumentNullException.ThrowIfNull(analysis);
            ArgumentNullException.ThrowIfNull(settings);

            // Harte Akzeptanz bei bekannter Hörspielserie
            if (analysis.IsKnownSeries)
            {
                return HoerspielScoreResult.Yes(
                    artistId,
                    HoerspielDecisionReason.KnownSeriesName,
                    100,
                    analysis.DebugInfo);
            }

            int score = 0;
            List<string> scoreParts = [];

            AddComponent(analysis.NameContainsQuery, settings.NameContainsBonus, "Name-Contains", ref score, scoreParts);
            AddComponent(analysis.HasNumberVariantMatch, settings.NameContainsBonus, "Zahlwort-Variante", ref score, scoreParts);
            AddComponent(analysis.HasExactWordMatch, settings.ExactWordMatchBonus, "Exaktes Wort-Match", ref score, scoreParts);

            if (extraComponents is not null)
            {
                foreach (HoerspielScoreComponent component in extraComponents)
                {
                    AddComponent(component.Condition, component.Bonus, component.Label, ref score, scoreParts);
                }
            }

            if (analysis.HasHoerspielAlbumStructure)
            {
                AddComponent(true, settings.AlbumStructureBonus, "Album-Struktur", ref score, scoreParts);
            }
            else
            {
                score += settings.NoAlbumPenalty;
                scoreParts.Add($"Keine Hörspiel-Alben: {settings.NoAlbumPenalty}");
            }

            string debugInfo = scoreParts.Count > 0
                ? string.Join("; ", scoreParts) + $" → Gesamt: {score}"
                : $"Keine Indikatoren gefunden → Gesamt: {score}";

            return score >= settings.MinimumScoreThreshold
                ? HoerspielScoreResult.Yes(artistId, HoerspielDecisionReason.None, score, debugInfo)
                : HoerspielScoreResult.No(artistId, HoerspielDecisionReason.None, score, debugInfo);
        }

        private static void AddComponent(bool condition, int bonus, string label, ref int score, List<string> scoreParts)
        {
            if (condition)
            {
                score += bonus;
                scoreParts.Add($"{label}: +{bonus}");
            }
        }
    }
}
